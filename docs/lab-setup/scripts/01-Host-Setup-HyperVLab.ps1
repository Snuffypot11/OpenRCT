<#
.SYNOPSIS
    Provisions the isolated Hyper-V virtual network, hardened WinNAT, and 4-VM OpenRCT testing lab.
.DESCRIPTION
    Run this script on your Windows 10/11 Pro/Enterprise Hyper-V host (32 GB RAM recommended).
    It creates:
      - Hyper-V Internal vSwitch 'OpenRCT-LabNet' (10.20.30.1/24 gateway, NO DNS configured)
      - Outbound-only WinNAT 'OpenRCT-LabNAT' (10.20.30.0/24)
      - Host isolation rules (Public profile, SMB/MS-Client disabled on lab vNIC, inbound VM->Host blocked)
      - Optional RFC1918 block rules preventing lab VMs from reaching your home/corporate LAN
      - 4 Generation 2 VMs with Dynamic Memory, thin VHDXs, Secure Boot, vTPM (workstations), and Guest Services:
          1. LAB-DC01 (2 vCPU, 1-2 GB RAM, 40 GB OS)
          2. LAB-CM01 (4 vCPU, 6-10 GB RAM, 80 GB OS + 120 GB E: Data)
          3. LAB-WS01 (2 vCPU, 2-4 GB RAM, 60 GB OS + 2 GB D: BitLocker test disk, vTPM)
          4. LAB-WS02 (2 vCPU, 2-4 GB RAM, 60 GB OS, vTPM)
.PARAMETER LabRoot
    Base directory on an NVMe/SSD drive where VM configs and VHDX files will be stored.
.PARAMETER ServerIsoPath
    Optional path to Windows Server 2022 or 2025 Evaluation ISO (attached to LAB-DC01 and LAB-CM01).
.PARAMETER WorkstationIsoPath
    Optional path to Windows 11 Enterprise Evaluation ISO (attached to LAB-WS01 and LAB-WS02).
.PARAMETER BlockHostLanSubnets
    Optional array of your home/corporate LAN CIDR ranges to block from lab VMs (e.g., @("192.168.0.0/16", "172.16.0.0/12")).
.PARAMETER RemoveLab
    Tears down all 4 lab VMs, VHDX files under LabRoot, firewall rules, WinNAT, and the OpenRCT-LabNet switch.
.EXAMPLE
    .\01-Host-Setup-HyperVLab.ps1 -LabRoot "D:\HyperV\OpenRCT-Lab" `
        -ServerIsoPath "D:\ISOs\WinServer2022_Eval.iso" `
        -WorkstationIsoPath "D:\ISOs\Win11_Enterprise_Eval.iso" `
        -BlockHostLanSubnets @("192.168.0.0/16")
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$LabRoot = "C:\HyperV\OpenRCT-Lab",
    [string]$ServerIsoPath = "",
    [string]$WorkstationIsoPath = "",
    [string[]]$BlockHostLanSubnets = @(),
    [switch]$RemoveLab
)

$ErrorActionPreference = "Stop"

$SwitchName = "OpenRCT-LabNet"
$NatName = "OpenRCT-LabNAT"
$GatewayIp = "10.20.30.1"
$SubnetPrefix = "10.20.30.0/24"
$FwRuleHostBlock = "OpenRCT Lab - Block VM to Host Inbound"
$FwRuleLanBlock = "OpenRCT Lab - Block Lab to Host LAN"

$VmDefinitions = @(
    @{
        Name       = "LAB-DC01"
        vCPU       = 2
        MinRam     = 1GB
        StartupRam = 2GB
        MaxRam     = 2GB
        OsDiskSize = 40GB
        DataDiskGb = 0
        EnableTpm  = $false
        IsoType    = "Server"
    },
    @{
        Name       = "LAB-CM01"
        vCPU       = 4
        MinRam     = 6GB
        StartupRam = 8GB
        MaxRam     = 10GB
        OsDiskSize = 80GB
        DataDiskGb = 120
        EnableTpm  = $false
        IsoType    = "Server"
    },
    @{
        Name       = "LAB-WS01"
        vCPU       = 2
        MinRam     = 2GB
        StartupRam = 4GB
        MaxRam     = 4GB
        OsDiskSize = 60GB
        DataDiskGb = 2
        EnableTpm  = $true
        IsoType    = "Workstation"
    },
    @{
        Name       = "LAB-WS02"
        vCPU       = 2
        MinRam     = 2GB
        StartupRam = 3GB
        MaxRam     = 4GB
        OsDiskSize = 60GB
        DataDiskGb = 0
        EnableTpm  = $true
        IsoType    = "Workstation"
    }
)

if (-not (Get-Module -ListAvailable -Name Hyper-V)) {
    throw "Hyper-V PowerShell module is not installed. Enable Hyper-V first: Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All"
}
Import-Module Hyper-V

if ($RemoveLab) {
    Write-Warning "Removing OpenRCT Lab VMs, virtual switch, WinNAT, and firewall rules..."
    foreach ($spec in $VmDefinitions) {
        $existing = Get-VM -Name $spec.Name -ErrorAction SilentlyContinue
        if ($existing) {
            if ($existing.State -ne "Off") {
                Stop-VM -Name $spec.Name -TurnOff -Force
            }
            Remove-VM -Name $spec.Name -Force
            Write-Host "  Removed VM: $($spec.Name)" -ForegroundColor Yellow
        }
    }

    Get-NetFirewallRule -DisplayName $FwRuleHostBlock -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Get-NetFirewallRule -DisplayName $FwRuleLanBlock -ErrorAction SilentlyContinue | Remove-NetFirewallRule

    $existingNat = Get-NetNat -Name $NatName -ErrorAction SilentlyContinue
    if ($existingNat) {
        Remove-NetNat -Name $NatName -Confirm:$false
        Write-Host "  Removed WinNAT: $NatName" -ForegroundColor Yellow
    }

    $existingSwitch = Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue
    if ($existingSwitch) {
        Remove-VMSwitch -Name $SwitchName -Force
        Write-Host "  Removed vSwitch: $SwitchName" -ForegroundColor Yellow
    }

    if (Test-Path $LabRoot) {
        Write-Host "  Deleting VM storage folder '$LabRoot'..." -ForegroundColor Yellow
        Remove-Item -Path $LabRoot -Recurse -Force
    }

    Write-Host "OpenRCT Lab cleanup complete." -ForegroundColor Green
    return
}

# Validate optional ISO paths
if (-not [string]::IsNullOrWhiteSpace($ServerIsoPath) -and -not (Test-Path $ServerIsoPath)) {
    throw "ServerIsoPath not found: $ServerIsoPath"
}
if (-not [string]::IsNullOrWhiteSpace($WorkstationIsoPath) -and -not (Test-Path $WorkstationIsoPath)) {
    throw "WorkstationIsoPath not found: $WorkstationIsoPath"
}

Write-Host "=== Step 1: Configuring Isolated Hyper-V Switch & WinNAT ===" -ForegroundColor Cyan

$vSwitch = Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue
if (-not $vSwitch) {
    $vSwitch = New-VMSwitch -Name $SwitchName -SwitchType Internal
    Write-Host "Created Internal vSwitch '$SwitchName'." -ForegroundColor Green
} else {
    Write-Host "vSwitch '$SwitchName' already exists." -ForegroundColor DarkGray
}

Start-Sleep -Seconds 2
$vNicName = "vEthernet ($SwitchName)"
$vNic = Get-NetAdapter -Name $vNicName -ErrorAction Stop

$existingIp = Get-NetIPAddress -InterfaceIndex $vNic.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -eq $GatewayIp }
if (-not $existingIp) {
    New-NetIPAddress -InterfaceIndex $vNic.ifIndex -IPAddress $GatewayIp -PrefixLength 24 | Out-Null
    Write-Host "Assigned NAT gateway IP $GatewayIp/24 to '$vNicName' (no DNS server configured)." -ForegroundColor Green
}

# Ensure host NEVER uses lab DNS on this adapter
Set-DnsClientServerAddress -InterfaceIndex $vNic.ifIndex -ResetServerAddresses

# Create WinNAT (note: Windows supports one active WinNAT per host by default)
$existingNat = Get-NetNat -Name $NatName -ErrorAction SilentlyContinue
if (-not $existingNat) {
    $otherNats = Get-NetNat -ErrorAction SilentlyContinue
    if ($otherNats) {
        Write-Warning "Another WinNAT instance already exists on this host: $($otherNats.Name -join ', ')."
        Write-Warning "Windows only supports a single WinNAT network at a time. Remove existing NAT or reuse its subnet if New-NetNat fails."
    }
    New-NetNat -Name $NatName -InternalIPInterfaceAddressPrefix $SubnetPrefix | Out-Null
    Write-Host "Created WinNAT '$NatName' for $SubnetPrefix." -ForegroundColor Green
} else {
    Write-Host "WinNAT '$NatName' already exists." -ForegroundColor DarkGray
}

Write-Host "=== Step 2: Hardening Host OS Isolation from Lab Network ===" -ForegroundColor Cyan

Set-NetConnectionProfile -InterfaceIndex $vNic.ifIndex -NetworkCategory Public -ErrorAction SilentlyContinue
foreach ($comp in @("ms_server", "ms_msclient", "ms_lltdio", "ms_rspndr")) {
    Disable-NetAdapterBinding -Name $vNicName -ComponentID $comp -ErrorAction SilentlyContinue
}

if (-not (Get-NetFirewallRule -DisplayName $FwRuleHostBlock -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $FwRuleHostBlock `
        -Direction Inbound `
        -InterfaceAlias $vNicName `
        -RemoteAddress $SubnetPrefix `
        -Action Block `
        -Profile Any | Out-Null
    Write-Host "Created host isolation firewall rule: '$FwRuleHostBlock'." -ForegroundColor Green
}

if ($BlockHostLanSubnets.Count -gt 0) {
    Get-NetFirewallRule -DisplayName $FwRuleLanBlock -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $FwRuleLanBlock `
        -Direction Outbound `
        -LocalAddress $SubnetPrefix `
        -RemoteAddress $BlockHostLanSubnets `
        -Action Block `
        -Profile Any | Out-Null
    Write-Host "Blocked lab subnet $SubnetPrefix from reaching LAN subnets: $($BlockHostLanSubnets -join ', ')" -ForegroundColor Green
}

Write-Host "=== Step 3: Creating 4 Generation 2 Lab VMs ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $LabRoot -Force | Out-Null

foreach ($spec in $VmDefinitions) {
    $name = $spec.Name
    if (Get-VM -Name $name -ErrorAction SilentlyContinue) {
        Write-Host "VM '$name' already exists; skipping creation." -ForegroundColor DarkGray
        continue
    }

    $vmDir = Join-Path $LabRoot $name
    $vhdDir = Join-Path $vmDir "Virtual Hard Disks"
    New-Item -ItemType Directory -Path $vhdDir -Force | Out-Null
    $osVhdPath = Join-Path $vhdDir "$name-OS.vhdx"

    Write-Host "Creating VM '$name' ($($spec.vCPU) vCPU, $($spec.StartupRam / 1GB) GB startup RAM)..." -ForegroundColor Cyan
    New-VM -Name $name `
        -Generation 2 `
        -MemoryStartupBytes $spec.StartupRam `
        -SwitchName $SwitchName `
        -Path $LabRoot `
        -NewVHDPath $osVhdPath `
        -NewVHDSizeBytes $spec.OsDiskSize | Out-Null

    Set-VM -Name $name `
        -ProcessorCount $spec.vCPU `
        -DynamicMemory `
        -MemoryMinimumBytes $spec.MinRam `
        -MemoryStartupBytes $spec.StartupRam `
        -MemoryMaximumBytes $spec.MaxRam `
        -AutomaticCheckpointsEnabled $false `
        -AutomaticStartAction Nothing `
        -AutomaticStopAction ShutDown

    Set-VMFirmware -VMName $name -EnableSecureBoot On -SecureBootTemplate "MicrosoftWindows"

    # Enable Guest Service Interface so Copy-VMFile works with zero network shares
    Enable-VMIntegrationService -VMName $name -Name "Guest Service Interface"

    # Optional secondary data VHDX (E: on LAB-CM01, D: BitLocker test disk on LAB-WS01)
    if ($spec.DataDiskGb -gt 0) {
        $dataVhdPath = Join-Path $vhdDir "$name-Data.vhdx"
        New-VHD -Path $dataVhdPath -SizeBytes ($spec.DataDiskGb * 1GB) -Dynamic | Out-Null
        Add-VMHardDiskDrive -VMName $name -Path $dataVhdPath
        Write-Host "  Attached $($spec.DataDiskGb) GB dynamic data disk: $dataVhdPath" -ForegroundColor Gray
    }

    # Enable vTPM on Windows 11 workstations (required for Win11 & BitLocker testing)
    if ($spec.EnableTpm) {
        Set-VMKeyProtector -VMName $name -NewLocalKeyProtector
        Enable-VMTPM -VMName $name
        Write-Host "  Enabled vTPM 2.0 on '$name'." -ForegroundColor Gray
    }

    # Attach installation ISO if supplied
    $isoToMount = if ($spec.IsoType -eq "Server") { $ServerIsoPath } else { $WorkstationIsoPath }
    if (-not [string]::IsNullOrWhiteSpace($isoToMount)) {
        $dvd = Add-VMDvdDrive -VMName $name -Path $isoToMount -Passthru
        $hd = Get-VMHardDiskDrive -VMName $name | Select-Object -First 1
        # Put Hard Drive first, DVD second: blank VHD auto-boots ISO, and post-install boots OS automatically
        Set-VMFirmware -VMName $name -BootOrder $hd, $dvd
        Write-Host "  Mounted ISO: $isoToMount" -ForegroundColor Gray
    } else {
        Add-VMDvdDrive -VMName $name | Out-Null
    }
}

Write-Host ""
Write-Host "Hyper-V lab provisioning complete!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open Hyper-V Manager and install Windows Server on LAB-DC01 first."
Write-Host "  2. Copy this scripts folder into LAB-DC01 using:"
Write-Host "     .\00-Host-Copy-FilesToVM.ps1 -VMName LAB-DC01 -SourcePath `"$PSScriptRoot`" -DestinationPath `"C:\LabScripts`""
Write-Host "  3. Follow docs/lab-setup/README.md Phase 1 onward."
