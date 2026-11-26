<#
.SYNOPSIS
    Runs an end-to-end readiness health check from LAB-CM01 validating every protocol, AD attribute,
    and MECM WMI dependency required to test all OpenRCT features.
.DESCRIPTION
    Run on LAB-CM01 while logged in as LAB\cmadmin after all 4 VMs are configured and the MECM
    client is installed on LAB-WS01 and LAB-WS02.
.PARAMETER SiteCode
    MECM 3-character site code (default: LAB).
.PARAMETER Workstations
    Target lab workstation names (default: LAB-WS01, LAB-WS02).
.EXAMPLE
    .\06-CM01-Verify-LabHealth.ps1 -SiteCode LAB
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$SiteCode = "LAB",
    [string[]]$Workstations = @("LAB-WS01", "LAB-WS02")
)

$ErrorActionPreference = "Continue"
$results = New-Object System.Collections.Generic.List[object]

function Add-CheckResult {
    param(
        [string]$Target,
        [string]$Check,
        [ValidateSet("PASS", "WARN", "FAIL")]
        [string]$Status,
        [string]$Details
    )
    $results.Add([pscustomobject]@{
        Target  = $Target
        Check   = $Check
        Status  = $Status
        Details = $Details
    })
}

Write-Host "=== Running OpenRCT Lab Health Verification (Site: $SiteCode) ===" -ForegroundColor Cyan

# 1. Endpoint protocol checks for each workstation
foreach ($ws in $Workstations) {
    # Ping / ICMP
    $pingOk = Test-Connection -ComputerName $ws -Count 1 -Quiet
    Add-CheckResult $ws "ICMP Ping" ($(if ($pingOk) { "PASS" } else { "FAIL" })) ($(if ($pingOk) { "Reachable" } else { "Unreachable (check ICMP firewall/power)" }))

    # WinRM (Interactive PowerShell)
    try {
        Test-WSMan -ComputerName $ws -ErrorAction Stop | Out-Null
        Add-CheckResult $ws "WinRM (TCP 5985)" "PASS" "PSRemoting responsive"
    } catch {
        Add-CheckResult $ws "WinRM (TCP 5985)" "FAIL" $_.Exception.Message
    }

    # WMI root\cimv2 & AllowRemoteRPC
    try {
        $os = Get-CimInstance -ComputerName $ws -ClassName Win32_OperatingSystem -ErrorAction Stop
        Add-CheckResult $ws "WMI (root\cimv2)" "PASS" "$($os.Caption) ($($os.BuildNumber))"
    } catch {
        Add-CheckResult $ws "WMI (root\cimv2)" "FAIL" $_.Exception.Message
    }

    # MECM Client WMI (root\ccm)
    try {
        $ccm = Get-CimInstance -ComputerName $ws -Namespace "root\ccm" -ClassName SMS_Client -ErrorAction Stop
        Add-CheckResult $ws "MECM Client (root\ccm)" "PASS" "Version $($ccm.ClientVersion)"
    } catch {
        Add-CheckResult $ws "MECM Client (root\ccm)" "FAIL" "Client not installed or WMI blocked"
    }

    # Defender WMI
    try {
        $def = Get-CimInstance -ComputerName $ws -Namespace "root\Microsoft\Windows\Defender" -ClassName MSFT_MpComputerStatus -ErrorAction Stop
        Add-CheckResult $ws "Windows Defender WMI" "PASS" "AMServiceEnabled=$($def.AMServiceEnabled)"
    } catch {
        Add-CheckResult $ws "Windows Defender WMI" "WARN" $_.Exception.Message
    }

    # SMB C$ & CCM Logs
    $logsPath = "\\$ws\C$\Windows\CCM\Logs"
    if (Test-Path $logsPath) {
        Add-CheckResult $ws "SMB Admin Share (C$ & CCM Logs)" "PASS" $logsPath
    } elseif (Test-Path "\\$ws\C$") {
        Add-CheckResult $ws "SMB Admin Share (C$ & CCM Logs)" "WARN" "C$ accessible, but CCM\Logs missing (client not installed?)"
    } else {
        Add-CheckResult $ws "SMB Admin Share (C$ & CCM Logs)" "FAIL" "Cannot access \\$ws\C$"
    }

    # Remote Registry (System Info -> Applications) + AllowRemoteRPC (msg.exe)
    try {
        $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $ws)
        $tsSub = $reg.OpenSubKey("SYSTEM\CurrentControlSet\Control\Terminal Server")
        $rpcVal = if ($tsSub) { $tsSub.GetValue("AllowRemoteRPC") } else { $null }
        $reg.Close()
        Add-CheckResult $ws "Remote Registry Service" "PASS" "OpenRemoteBaseKey succeeded"
        if ($rpcVal -eq 1) {
            Add-CheckResult $ws "AllowRemoteRPC (msg.exe)" "PASS" "AllowRemoteRPC = 1"
        } else {
            Add-CheckResult $ws "AllowRemoteRPC (msg.exe)" "WARN" "AllowRemoteRPC is '$rpcVal' (expected 1 for Send Notification)"
        }
    } catch {
        Add-CheckResult $ws "Remote Registry Service" "FAIL" $_.Exception.Message
    }
}

# 2. Active Directory Checks (Orphan PC, Windows LAPS, Legacy LAPS, BitLocker)
try {
    Import-Module ActiveDirectory -ErrorAction Stop

    $orphan = Get-ADComputer -Filter "Name -eq 'LAB-ORPHAN-AD'" -ErrorAction SilentlyContinue
    Add-CheckResult "LAB-DC01" "AD Orphan Device (LAB-ORPHAN-AD)" ($(if ($orphan) { "PASS" } else { "WARN" })) ($(if ($orphan) { "Present for AD Cleanup Dashboard" } else { "Not found" }))

    # LAB-WS01 Windows LAPS cleartext attribute (msLAPS-Password)
    $ws1Ad = Get-ADComputer -Identity "LAB-WS01" -Properties "msLAPS-Password", "msLAPS-PasswordExpirationTime" -ErrorAction SilentlyContinue
    $hasWinLaps = $ws1Ad -and -not [string]::IsNullOrWhiteSpace($ws1Ad."msLAPS-Password")
    Add-CheckResult "LAB-WS01" "Windows LAPS (msLAPS-Password)" ($(if ($hasWinLaps) { "PASS" } else { "WARN" })) ($(if ($hasWinLaps) { "Cleartext password present in AD" } else { "Empty (check ADPasswordEncryptionEnabled=0 & Invoke-LapsPolicyProcessing)" }))

    # LAB-WS02 Legacy LAPS attribute (ms-Mcs-AdmPwd)
    $ws2Ad = Get-ADComputer -Identity "LAB-WS02" -Properties "ms-Mcs-AdmPwd", "ms-Mcs-AdmPwdExpirationTime" -ErrorAction SilentlyContinue
    $hasLegacyLaps = $ws2Ad -and -not [string]::IsNullOrWhiteSpace($ws2Ad."ms-Mcs-AdmPwd")
    Add-CheckResult "LAB-WS02" "Legacy LAPS (ms-Mcs-AdmPwd)" ($(if ($hasLegacyLaps) { "PASS" } else { "WARN" })) ($(if ($hasLegacyLaps) { "Legacy LAPS password present in AD" } else { "Not populated (install LAPS.x64.msi or leave blank for No-LAPS slice)" }))

    # LAB-WS01 BitLocker Recovery Information in AD
    if ($ws1Ad) {
        $blKeys = Get-ADObject -Filter "objectClass -eq 'msFVE-RecoveryInformation'" -SearchBase $ws1Ad.DistinguishedName -ErrorAction SilentlyContinue
        $hasBl = ($blKeys | Measure-Object).Count -gt 0
        Add-CheckResult "LAB-WS01" "BitLocker Recovery Key in AD" ($(if ($hasBl) { "PASS" } else { "WARN" })) ($(if ($hasBl) { "$((@($blKeys)).Count) key(s) escrowed in AD" } else { "No msFVE-RecoveryInformation object under LAB-WS01" }))
    }
} catch {
    Add-CheckResult "LAB-DC01" "ActiveDirectory Module Checks" "FAIL" $_.Exception.Message
}

# 3. MECM SMS Provider WMI Checks
$smsNs = "root\SMS\site_$SiteCode"
try {
    $cols = @(Get-CimInstance -Namespace $smsNs -Query "SELECT CollectionID, Name FROM SMS_Collection WHERE CollectionType = 2" -ErrorAction Stop)
    Add-CheckResult "LAB-CM01" "SMS Provider ($smsNs)" "PASS" "$($cols.Count) Device Collection(s) accessible"

    $devs = @(Get-CimInstance -Namespace $smsNs -Query "SELECT ResourceID, Name, Client FROM SMS_R_System WHERE Name LIKE 'LAB-WS%'" -ErrorAction Stop)
    $activeClients = @($devs | Where-Object { $_.Client -eq 1 })
    Add-CheckResult "LAB-CM01" "MECM Workstation Records" ($(if ($activeClients.Count -ge 2) { "PASS" } else { "WARN" })) "$($activeClients.Count)/$($devs.Count) LAB-WS* device(s) have active Client=1"

    $macs = @(Get-CimInstance -Namespace $smsNs -Query "SELECT MACAddress FROM SMS_G_System_NETWORK_ADAPTER_CONFIGURATION WHERE MACAddress IS NOT NULL" -ErrorAction SilentlyContinue)
    Add-CheckResult "LAB-CM01" "Hardware Inventory (MACs for WoL)" ($(if ($macs.Count -gt 0) { "PASS" } else { "WARN" })) "$($macs.Count) MAC address record(s) inventoried"

    $sugs = @(Get-CimInstance -Namespace $smsNs -Query "SELECT LocalizedDisplayName FROM SMS_AuthorizationList" -ErrorAction SilentlyContinue)
    Add-CheckResult "LAB-CM01" "Software Update Groups (SUP)" ($(if ($sugs.Count -gt 0) { "PASS" } else { "WARN" })) "$($sugs.Count) Software Update Group(s) found"
} catch {
    Add-CheckResult "LAB-CM01" "SMS Provider ($smsNs)" "FAIL" $_.Exception.Message
}

# 4. OpenRCT Installation & Optional PsExec Check
$openRctExe = "$env:ProgramFiles\OpenRCT\OpenRCT.App.exe"
Add-CheckResult "LAB-CM01" "OpenRCT Installation" ($(if (Test-Path $openRctExe) { "PASS" } else { "WARN" })) ($(if (Test-Path $openRctExe) { "Installed at $openRctExe" } else { "Not installed yet" }))

$psExecPath = "C:\Tools\PsExec64.exe"
Add-CheckResult "LAB-CM01" "Optional Sysinternals PsExec64" ($(if (Test-Path $psExecPath) { "PASS" } else { "WARN" })) ($(if (Test-Path $psExecPath) { "Present at $psExecPath" } else { "Optional: place PsExec64.exe at C:\Tools\ for Interactive CMD" }))

Write-Host ""
foreach ($row in $results) {
    $color = switch ($row.Status) {
        "PASS" { "Green" }
        "WARN" { "Yellow" }
        "FAIL" { "Red" }
    }
    Write-Host ("[{0,-4}] {1,-10} | {2,-34} | {3}" -f $row.Status, $row.Target, $row.Check, $row.Details) -ForegroundColor $color
}

$failCount = @($results | Where-Object { $_.Status -eq "FAIL" }).Count
$warnCount = @($results | Where-Object { $_.Status -eq "WARN" }).Count
$passCount = @($results | Where-Object { $_.Status -eq "PASS" }).Count
Write-Host ""
Write-Host "Summary: $passCount PASS, $warnCount WARN, $failCount FAIL" -ForegroundColor Cyan
