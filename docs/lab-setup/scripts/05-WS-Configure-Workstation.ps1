<#
.SYNOPSIS
    Configures LAB-WS01 (Primary) or LAB-WS02 (Secondary) for end-to-end OpenRCT testing.
.DESCRIPTION
    Run inside LAB-WS01 or LAB-WS02 after installing Windows 11 Enterprise.
      - Phase JoinDomain       : Renames VM to LAB-WS01 or LAB-WS02, joins lab.local into OU=Workstations, and reboots.
      - Phase ConfigureEndpoint: Applies Group Policy, verifies WinRM / RemoteRegistry / AllowRemoteRPC / Local Admins,
                                 and configures role-specific test features:
                                   * Primary   (LAB-WS01): Initializes 2 GB D:\ disk, enables BitLocker + AD Recovery Key
                                                           escrow, and runs Invoke-LapsPolicyProcessing (Windows LAPS).
                                   * Secondary (LAB-WS02): Installs Legacy Microsoft LAPS MSI (if provided) and forces GPUpdate.
.PARAMETER Role
    Primary (LAB-WS01) or Secondary (LAB-WS02).
.PARAMETER Phase
    JoinDomain or ConfigureEndpoint.
.PARAMETER LegacyLapsMsiPath
    Optional path to LAPS.x64.msi when configuring LAB-WS02 (Secondary).
.PARAMETER DomainCredential
    Domain Admin credentials (e.g., LAB\cmadmin) for domain join.
.EXAMPLE
    .\05-WS-Configure-Workstation.ps1 -Role Primary -Phase JoinDomain
.EXAMPLE
    .\05-WS-Configure-Workstation.ps1 -Role Primary -Phase ConfigureEndpoint
.EXAMPLE
    .\05-WS-Configure-Workstation.ps1 -Role Secondary -Phase ConfigureEndpoint -LegacyLapsMsiPath "C:\Install\LAPS.x64.msi"
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Primary", "Secondary")]
    [string]$Role,

    [Parameter(Mandatory = $true)]
    [ValidateSet("JoinDomain", "ConfigureEndpoint")]
    [string]$Phase,

    [string]$LegacyLapsMsiPath = "",

    [PSCredential]$DomainCredential
)

$ErrorActionPreference = "Stop"

$TargetName = if ($Role -eq "Primary") { "LAB-WS01" } else { "LAB-WS02" }
$DomainFqdn = "lab.local"
$WorkstationsOu = "OU=Workstations,DC=lab,DC=local"
$DcIp = "10.20.30.10"

if ($Phase -eq "JoinDomain") {
    Write-Host "=== Joining $TargetName to $DomainFqdn ($WorkstationsOu) ===" -ForegroundColor Cyan

    # Verify DHCP gave us a 10.20.30.x address and DC DNS
    $adapter = Get-NetAdapter | Where-Object { $_.Status -eq "Up" } | Select-Object -First 1
    if (-not $adapter) {
        throw "No active network adapter found."
    }

    if (-not (Test-Connection -ComputerName $DcIp -Count 2 -Quiet)) {
        Write-Warning "Cannot ping $DcIp yet. Refreshing DHCP lease..."
        ipconfig /renew | Out-Null
        Start-Sleep -Seconds 3
    }

    $cs = Get-CimInstance Win32_ComputerSystem
    if ($cs.PartOfDomain -and $cs.Domain -eq $DomainFqdn -and $env:COMPUTERNAME -eq $TargetName) {
        Write-Host "$TargetName is already joined to $DomainFqdn." -ForegroundColor DarkGray
        return
    }

    if (-not $DomainCredential) {
        $DomainCredential = Get-Credential -UserName "LAB\cmadmin" -Message "Enter Domain Admin credentials to join $TargetName to $DomainFqdn"
    }

    Add-Computer -DomainName $DomainFqdn -NewName $TargetName -OUPath $WorkstationsOu -Credential $DomainCredential -Restart -Force
    return
}

if ($Phase -eq "ConfigureEndpoint") {
    Write-Host "=== Configuring Endpoint Management Prerequisites on $TargetName ===" -ForegroundColor Cyan

    # 1. Force Group Policy refresh
    Write-Host "Running gpupdate /force..." -ForegroundColor Gray
    gpupdate /force | Out-Null

    # 2. Ensure LAB\cmadmin and LAB\cm_push are in Local Administrators (belt-and-suspenders alongside GPO)
    foreach ($acct in @("LAB\cmadmin", "LAB\cm_push")) {
        try {
            $existing = Get-LocalGroupMember -Group "Administrators" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ieq $acct }
            if (-not $existing) {
                Add-LocalGroupMember -Group "Administrators" -Member $acct
                Write-Host "  Added '$acct' to local Administrators." -ForegroundColor Gray
            }
        } catch {
            Write-Warning "Could not add '$acct' to Administrators: $($_.Exception.Message)"
        }
    }

    # 3. Enable PowerShell Remoting (WinRM HTTP 5985) for OpenRCT Interactive PowerShell Prompt
    Enable-PSRemoting -Force -SkipNetworkProfileCheck | Out-Null
    Write-Host "  Enabled WinRM / PSRemoting." -ForegroundColor Gray

    # 4. Ensure RemoteRegistry service is Automatic & Running (for OpenRCT System Info -> Applications)
    Set-Service -Name "RemoteRegistry" -StartupType Automatic
    Start-Service -Name "RemoteRegistry"
    Write-Host "  Started RemoteRegistry service." -ForegroundColor Gray

    # 5. Ensure AllowRemoteRPC = 1 (for OpenRCT Send Notification / msg.exe)
    $tsKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server"
    Set-ItemProperty -Path $tsKey -Name "AllowRemoteRPC" -Type DWord -Value 1
    Write-Host "  Verified AllowRemoteRPC = 1." -ForegroundColor Gray

    if ($Role -eq "Primary") {
        Write-Host "=== Configuring Primary Workstation Features (LAB-WS01: Windows LAPS + BitLocker Test Disk) ===" -ForegroundColor Cyan

        # Initialize 2 GB secondary disk as D:\ for fast BitLocker AD recovery key escrow testing
        if (-not (Test-Path "D:\")) {
            $rawDisk = Get-Disk | Where-Object { $_.PartitionStyle -eq "RAW" -and $_.Number -gt 0 } | Select-Object -First 1
            if ($rawDisk) {
                $rawDisk | Initialize-Disk -PartitionStyle GPT -PassThru |
                    New-Partition -DriveLetter D -UseMaximumSize |
                    Format-Volume -FileSystem NTFS -NewFileSystemLabel "BitLockerTest" -Confirm:$false | Out-Null
                Write-Host "  Initialized 2 GB D:\ (BitLockerTest) drive." -ForegroundColor Gray
            } else {
                Write-Warning "No 2 GB RAW data disk found for D:\. Attach the 2 GB VHDX to test BitLocker recovery keys."
            }
        }

        if (Test-Path "D:\") {
            $blVol = Get-BitLockerVolume -MountPoint "D:" -ErrorAction SilentlyContinue
            if ($blVol -and $blVol.VolumeStatus -eq "FullyDecrypted") {
                Write-Host "  Enabling BitLocker (UsedSpaceOnly) on D:\ with Recovery Password protector..." -ForegroundColor Cyan
                Enable-BitLocker -MountPoint "D:" -EncryptionMethod XtsAes128 -UsedSpaceOnly -RecoveryPasswordProtector -SkipHardwareTest | Out-Null
                Start-Sleep -Seconds 3
                $blVol = Get-BitLockerVolume -MountPoint "D:"
            }

            if ($blVol) {
                $recProtector = $blVol.KeyProtector | Where-Object { $_.KeyProtectorType -eq "RecoveryPassword" } | Select-Object -First 1
                if ($recProtector) {
                    Backup-BitLockerKeyProtector -MountPoint "D:" -KeyProtectorId $recProtector.KeyProtectorId | Out-Null
                    Write-Host "  Escrowed BitLocker Recovery Key ($($recProtector.KeyProtectorId)) to Active Directory!" -ForegroundColor Green
                }
            }
        }

        # Trigger Windows LAPS password generation/backup immediately
        if (Get-Command Invoke-LapsPolicyProcessing -ErrorAction SilentlyContinue) {
            Write-Host "  Triggering Windows LAPS policy processing..." -ForegroundColor Cyan
            Invoke-LapsPolicyProcessing
            Write-Host "  Windows LAPS policy processed. Verify msLAPS-Password in AD." -ForegroundColor Green
        } else {
            Write-Warning "Invoke-LapsPolicyProcessing not available on this Windows build. Ensure Windows 11 23H2/24H2 or April 2023+ CU is installed."
        }
    } else {
        Write-Host "=== Configuring Secondary Workstation Features (LAB-WS02: Legacy LAPS x64) ===" -ForegroundColor Cyan

        if (-not [string]::IsNullOrWhiteSpace($LegacyLapsMsiPath) -and (Test-Path $LegacyLapsMsiPath)) {
            Write-Host "  Installing Legacy Microsoft LAPS from '$LegacyLapsMsiPath'..." -ForegroundColor Cyan
            $proc = Start-Process -FilePath "msiexec.exe" -ArgumentList "/i `"$LegacyLapsMsiPath`" /qn /norestart" -Wait -PassThru
            if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 3010) {
                throw "Legacy LAPS MSI installation failed with exit code $($proc.ExitCode)."
            }
            gpupdate /force | Out-Null
            Write-Host "  Legacy LAPS installed and GPUpdate executed. Verify ms-Mcs-AdmPwd in AD." -ForegroundColor Green
        } else {
            Write-Host "  No -LegacyLapsMsiPath specified. LAB-WS02 will remain without Legacy LAPS until installed (which also lets you test the 'No LAPS' dashboard slice)." -ForegroundColor Yellow
        }
    }

    Write-Host ""
    Write-Host "$TargetName endpoint configuration complete!" -ForegroundColor Green
}
