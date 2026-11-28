<#
.SYNOPSIS
    Configures DNS, DHCP, OUs, test users/groups/orphan computer, MECM System Management container,
    LAPS schemas/permissions, and OpenCMTools endpoint management GPOs on LAB-DC01.
.DESCRIPTION
    Run on LAB-DC01 AFTER Active Directory promotion has rebooted and you are logged in as LAB\Administrator.
    You can re-run this script safely with -GrantCm01SystemManagementAcl after LAB-CM01 joins the domain
    to grant LAB-CM01$ Full Control on CN=System Management,CN=System,DC=lab,DC=local, or with
    -ApplyGpoSecurityFiltering after LAB-WS01 and LAB-WS02 join the domain to target Windows LAPS vs Legacy LAPS.
.PARAMETER DefaultLabPassword
    Password assigned to lab test accounts (cmadmin, cm_push, testuser1, testuser2). Prompted if omitted.
.PARAMETER GrantCm01SystemManagementAcl
    Re-run switch: grants LAB-CM01$ Full Control on CN=System Management,CN=System,DC=lab,DC=local.
.PARAMETER ApplyGpoSecurityFiltering
    Re-run switch: restricts GPO-Windows-LAPS to LAB-WS01$ and GPO-Legacy-LAPS to LAB-WS02$.
.EXAMPLE
    .\03-DC01-Configure-Domain-GPO-LAPS.ps1
.EXAMPLE
    .\03-DC01-Configure-Domain-GPO-LAPS.ps1 -GrantCm01SystemManagementAcl
.EXAMPLE
    .\03-DC01-Configure-Domain-GPO-LAPS.ps1 -ApplyGpoSecurityFiltering
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [SecureString]$DefaultLabPassword,
    [switch]$GrantCm01SystemManagementAcl,
    [switch]$ApplyGpoSecurityFiltering
)

$ErrorActionPreference = "Stop"

Import-Module ActiveDirectory
Import-Module GroupPolicy
Import-Module DnsServer
Import-Module DhcpServer
Import-Module NetSecurity

$DomainDn = (Get-ADDomain).DistinguishedName
$DomainFqdn = (Get-ADDomain).DNSRoot
$WorkstationsOu = "OU=Workstations,$DomainDn"
$ServersOu = "OU=Servers,$DomainDn"
$ServiceAccountsOu = "OU=ServiceAccounts,$DomainDn"

function Grant-SystemManagementAccess {
    param([string]$ComputerName)
    $sysMgmtDn = "CN=System Management,CN=System,$DomainDn"
    $comp = Get-ADComputer -Identity $ComputerName -ErrorAction SilentlyContinue
    if (-not $comp) {
        Write-Warning "Computer account '$ComputerName' does not exist in AD yet. Re-run with -GrantCm01SystemManagementAcl after $ComputerName joins $DomainFqdn."
        return
    }

    $aclPath = "AD:$sysMgmtDn"
    $acl = Get-Acl -Path $aclPath
    $sid = New-Object System.Security.Principal.SecurityIdentifier($comp.SID)
    $rule = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
        $sid,
        [System.DirectoryServices.ActiveDirectoryRights]::GenericAll,
        [System.Security.AccessControl.AccessControlType]::Allow,
        [System.DirectoryServices.ActiveDirectorySecurityInheritance]::All
    )
    $acl.AddAccessRule($rule)
    Set-Acl -Path $aclPath -AclObject $acl
    Write-Host "Granted '${ComputerName}$' Full Control (This object and all descendant objects) on '$sysMgmtDn'." -ForegroundColor Green
}

function Set-LapsGpoFiltering {
    $ws1 = Get-ADComputer -Identity "LAB-WS01" -ErrorAction SilentlyContinue
    $ws2 = Get-ADComputer -Identity "LAB-WS02" -ErrorAction SilentlyContinue

    if ($ws1) {
        Set-GPPermission -Name "GPO-Windows-LAPS" -TargetName "Authenticated Users" -TargetType Group -PermissionLevel GpoRead -Replace | Out-Null
        Set-GPPermission -Name "GPO-Windows-LAPS" -TargetName "LAB-WS01$" -TargetType Computer -PermissionLevel GpoApply | Out-Null
        Write-Host "Scoped 'GPO-Windows-LAPS' to apply only to LAB-WS01$." -ForegroundColor Green
    } else {
        Write-Warning "LAB-WS01 is not domain-joined yet; skipping GPO-Windows-LAPS security filtering."
    }

    if ($ws2) {
        Set-GPPermission -Name "GPO-Legacy-LAPS" -TargetName "Authenticated Users" -TargetType Group -PermissionLevel GpoRead -Replace | Out-Null
        Set-GPPermission -Name "GPO-Legacy-LAPS" -TargetName "LAB-WS02$" -TargetType Computer -PermissionLevel GpoApply | Out-Null
        Write-Host "Scoped 'GPO-Legacy-LAPS' to apply only to LAB-WS02$." -ForegroundColor Green
    } else {
        Write-Warning "LAB-WS02 is not domain-joined yet; skipping GPO-Legacy-LAPS security filtering."
    }
}

if ($GrantCm01SystemManagementAcl) {
    Grant-SystemManagementAccess -ComputerName "LAB-CM01"
    return
}

if ($ApplyGpoSecurityFiltering) {
    Set-LapsGpoFiltering
    return
}

if (-not $DefaultLabPassword) {
    $DefaultLabPassword = Read-Host -Prompt "Enter password to set for lab accounts (cmadmin, cm_push, testuser1, testuser2)" -AsSecureString
}

Write-Host "=== Step 1: Configuring DNS Forwarders & Reverse Lookup Zone ===" -ForegroundColor Cyan
Set-DnsServerForwarder -IPAddress @("1.1.1.1", "8.8.8.8") | Out-Null
$revZone = Get-DnsServerZone -Name "30.20.10.in-addr.arpa" -ErrorAction SilentlyContinue
if (-not $revZone) {
    Add-DnsServerPrimaryZone -NetworkId "10.20.30.0/24" -ReplicationScope Domain
    Write-Host "Created AD-integrated Reverse Lookup Zone for 10.20.30.0/24." -ForegroundColor Green
}

# Ensure DC points cleanly to itself for DNS
$adapter = Get-NetAdapter | Where-Object { $_.Status -eq "Up" } | Select-Object -First 1
if ($adapter) {
    Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses @("127.0.0.1")
}

Write-Host "=== Step 2: Authorizing & Configuring DHCP Scope (10.20.30.100 - 10.20.30.150) ===" -ForegroundColor Cyan
try {
    Add-DhcpServerInDC -DnsName "LAB-DC01.$DomainFqdn" -IPAddress "10.20.30.10" -ErrorAction SilentlyContinue
} catch { }

Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\ServerManager\Roles\12" -Name ConfigurationState -Value 2 -ErrorAction SilentlyContinue

$scope = Get-DhcpServerv4Scope -ScopeId "10.20.30.0" -ErrorAction SilentlyContinue
if (-not $scope) {
    Add-DhcpServerv4Scope `
        -Name "OpenCMTools Lab Workstations" `
        -StartRange "10.20.30.100" `
        -EndRange "10.20.30.150" `
        -SubnetMask "255.255.255.0" `
        -LeaseDuration (New-TimeSpan -Days 8) `
        -State Active
    Write-Host "Created DHCP Scope 10.20.30.100 - 10.20.30.150." -ForegroundColor Green
}

Set-DhcpServerv4OptionValue -ScopeId "10.20.30.0" `
    -Router "10.20.30.1" `
    -DnsServer "10.20.30.10" `
    -DnsDomain $DomainFqdn

Restart-Service DHCPServer

Write-Host "=== Step 3: Creating OUs, Security Groups, Lab Accounts & Dummy Orphan Computer ===" -ForegroundColor Cyan
foreach ($ouName in @("Servers", "Workstations", "ServiceAccounts")) {
    if (-not (Get-ADOrganizationalUnit -Filter "Name -eq '$ouName'" -SearchBase $DomainDn -ErrorAction SilentlyContinue)) {
        New-ADOrganizationalUnit -Name $ouName -Path $DomainDn -ProtectedFromAccidentalDeletion $false
        Write-Host "  Created OU=$ouName" -ForegroundColor Gray
    }
}

$groups = @(
    @{ Name = "SG-Lab-Admins"; Description = "OpenCMTools & MECM Lab Administrators" },
    @{ Name = "SG-Lab-TestGroup1"; Description = "Sample AD Security Group for OpenCMTools testing" },
    @{ Name = "SG-Lab-TestGroup2"; Description = "Secondary AD Security Group for OpenCMTools testing" }
)
foreach ($g in $groups) {
    if (-not (Get-ADGroup -Filter "Name -eq '$($g.Name)'" -ErrorAction SilentlyContinue)) {
        New-ADGroup -Name $g.Name -GroupScope Global -GroupCategory Security -Path $ServiceAccountsOu -Description $g.Description
        Write-Host "  Created Group: $($g.Name)" -ForegroundColor Gray
    }
}

$users = @(
    @{ Sam = "cmadmin"; Name = "CM Admin"; Path = $ServiceAccountsOu; Desc = "MECM & OpenCMTools Primary Admin Account" },
    @{ Sam = "cm_push"; Name = "CM Client Push"; Path = $ServiceAccountsOu; Desc = "MECM Client Push Installation Account" },
    @{ Sam = "testuser1"; Name = "Lab User One"; Path = $WorkstationsOu; Desc = "Primary interactive workstation test user" },
    @{ Sam = "testuser2"; Name = "Lab User Two"; Path = $WorkstationsOu; Desc = "Secondary profile test user (for User Profiles tab)" }
)
foreach ($u in $users) {
    if (-not (Get-ADUser -Filter "SamAccountName -eq '$($u.Sam)'" -ErrorAction SilentlyContinue)) {
        New-ADUser `
            -Name $u.Name `
            -SamAccountName $u.Sam `
            -UserPrincipalName "$($u.Sam)@$DomainFqdn" `
            -Path $u.Path `
            -Description $u.Desc `
            -AccountPassword $DefaultLabPassword `
            -Enabled $true `
            -PasswordNeverExpires $true
        Write-Host "  Created User: LAB\$($u.Sam)" -ForegroundColor Gray
    }
}

foreach ($adminGroup in @("Domain Admins", "Enterprise Admins", "Schema Admins", "SG-Lab-Admins")) {
    Add-ADGroupMember -Identity $adminGroup -Members "cmadmin" -ErrorAction SilentlyContinue
}

# Dummy AD-only computer to populate AD Cleanup ("Active Directory Only") and LAPS ("No LAPS") dashboards
if (-not (Get-ADComputer -Filter "Name -eq 'LAB-ORPHAN-AD'" -ErrorAction SilentlyContinue)) {
    New-ADComputer `
        -Name "LAB-ORPHAN-AD" `
        -SamAccountName "LAB-ORPHAN-AD$" `
        -Path $WorkstationsOu `
        -Enabled $true `
        -Description "AD-only orphan device for testing OpenCMTools AD Cleanup & LAPS Dashboards"
    Write-Host "  Created dummy AD-only computer: LAB-ORPHAN-AD" -ForegroundColor Gray
}

Write-Host "=== Step 4: Creating MECM System Management Container ===" -ForegroundColor Cyan
$sysContainer = "CN=System,$DomainDn"
$sysMgmtDn = "CN=System Management,$sysContainer"
if (-not (Get-ADObject -Filter "Name -eq 'System Management'" -SearchBase $sysContainer -ErrorAction SilentlyContinue)) {
    New-ADObject -Name "System Management" -Type container -Path $sysContainer
    Write-Host "Created '$sysMgmtDn'." -ForegroundColor Green
}
Grant-SystemManagementAccess -ComputerName "LAB-CM01"

Write-Host "=== Step 5: Extending Windows LAPS (and optional Legacy LAPS) Schema & Permissions ===" -ForegroundColor Cyan
if (Get-Command Update-LapsADSchema -ErrorAction SilentlyContinue) {
    Update-LapsADSchema -Confirm:$false
    Set-LapsADComputerSelfPermission -Identity $WorkstationsOu | Out-Null
    Write-Host "Extended Windows LAPS schema and delegated computer self-write permissions on '$WorkstationsOu'." -ForegroundColor Green
} else {
    Write-Warning "Windows LAPS PowerShell module ('LAPS') not found on this OS build."
    Write-Warning "Install the latest Windows Server 2022 Cumulative Update (April 2023+) or use Windows Server 2025, then run:"
    Write-Warning "  Update-LapsADSchema; Set-LapsADComputerSelfPermission -Identity `"$WorkstationsOu`""
}

if (Get-Module -ListAvailable -Name AdmPwd.PS) {
    Import-Module AdmPwd.PS
    Update-AdmPwdADSchema | Out-Null
    Set-AdmPwdComputerSelfPermission -OrgUnit $WorkstationsOu | Out-Null
    Write-Host "Extended Legacy Microsoft LAPS schema and delegated self-write permissions on '$WorkstationsOu'." -ForegroundColor Green
} else {
    Write-Host "Note: Legacy LAPS ('AdmPwd.PS') not installed on LAB-DC01 yet. Install LAPS.x64.msi (Management Tools) on LAB-DC01 if you want to test Legacy LAPS on LAB-WS02, then run:" -ForegroundColor Yellow
    Write-Host "  Import-Module AdmPwd.PS; Update-AdmPwdADSchema; Set-AdmPwdComputerSelfPermission -OrgUnit `"$WorkstationsOu`"" -ForegroundColor Yellow
}

Write-Host "=== Step 6: Creating & Linking OpenCMTools Group Policy Objects ===" -ForegroundColor Cyan

function Ensure-GpoLinked {
    param([string]$GpoName, [string]$TargetOu)
    $gpo = Get-GPO -Name $GpoName -ErrorAction SilentlyContinue
    if (-not $gpo) {
        $gpo = New-GPO -Name $GpoName
        Write-Host "  Created GPO: $GpoName" -ForegroundColor Gray
    }
    $inheritance = Get-GPInheritance -Target $TargetOu
    if (-not ($inheritance.GpoLinks | Where-Object { $_.DisplayName -eq $GpoName })) {
        New-GPLink -Name $GpoName -Target $TargetOu -LinkEnabled Yes | Out-Null
        Write-Host "  Linked '$GpoName' -> $TargetOu" -ForegroundColor Gray
    }
    return $gpo
}

# 6a. GPO-OpenCMTools-EndpointManagement (WinRM, RemoteRegistry, AllowRemoteRPC for msg.exe, Domain Firewall Rules)
$gpoMgmt = "GPO-OpenCMTools-EndpointManagement"
Ensure-GpoLinked -GpoName $gpoMgmt -TargetOu $WorkstationsOu | Out-Null

# AllowRemoteRPC = 1 (required for msg.exe Send Notification)
Set-GPRegistryValue -Name $gpoMgmt `
    -Key "HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server" `
    -ValueName "AllowRemoteRPC" -Type DWord -Value 1 | Out-Null

# RemoteRegistry service -> Automatic (Start = 2) for OpenCMTools System Info -> Applications tab
Set-GPRegistryValue -Name $gpoMgmt `
    -Key "HKLM\SYSTEM\CurrentControlSet\Services\RemoteRegistry" `
    -ValueName "Start" -Type DWord -Value 2 | Out-Null

# WinRM service -> Automatic (Start = 2) + AllowAutoConfig listener
Set-GPRegistryValue -Name $gpoMgmt `
    -Key "HKLM\SYSTEM\CurrentControlSet\Services\WinRM" `
    -ValueName "Start" -Type DWord -Value 2 | Out-Null
Set-GPRegistryValue -Name $gpoMgmt `
    -Key "HKLM\SOFTWARE\Policies\Microsoft\Windows\WinRM\Service" `
    -ValueName "AllowAutoConfig" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoMgmt `
    -Key "HKLM\SOFTWARE\Policies\Microsoft\Windows\WinRM\Service" `
    -ValueName "IPv4Filter" -Type String -Value "*" | Out-Null
Set-GPRegistryValue -Name $gpoMgmt `
    -Key "HKLM\SOFTWARE\Policies\Microsoft\Windows\WinRM\Service" `
    -ValueName "IPv6Filter" -Type String -Value "*" | Out-Null

# Windows Defender Firewall Rules inside GPO via Open-NetGPO
$gpoSession = Open-NetGPO -PolicyStore "$DomainFqdn\$gpoMgmt"
try {
    $fwSpecs = @(
        @{ Name = "OpenCMTools-Allow-ICMPv4"; DisplayName = "OpenCMTools Lab - Allow ICMPv4 Ping"; Protocol = "ICMPv4"; IcmpType = "8" },
        @{ Name = "OpenCMTools-Allow-SMB"; DisplayName = "OpenCMTools Lab - Allow SMB (TCP 445)"; Protocol = "TCP"; LocalPort = "445" },
        @{ Name = "OpenCMTools-Allow-WinRM"; DisplayName = "OpenCMTools Lab - Allow WinRM HTTP (TCP 5985)"; Protocol = "TCP"; LocalPort = "5985" },
        @{ Name = "OpenCMTools-Allow-RPC-EPM"; DisplayName = "OpenCMTools Lab - Allow RPC Endpoint Mapper (TCP 135)"; Protocol = "TCP"; LocalPort = "135" },
        @{ Name = "OpenCMTools-Allow-RPC-Dynamic"; DisplayName = "OpenCMTools Lab - Allow WMI/DCOM/EventLog Dynamic RPC"; Protocol = "TCP"; LocalPort = "49152-65535" }
    )
    foreach ($fw in $fwSpecs) {
        if (-not (Get-NetFirewallRule -GPOSession $gpoSession -Name $fw.Name -ErrorAction SilentlyContinue)) {
            $params = @{
                GPOSession  = $gpoSession
                Name        = $fw.Name
                DisplayName = $fw.DisplayName
                Direction   = "Inbound"
                Action      = "Allow"
                Profile     = "Domain"
                Protocol    = $fw.Protocol
            }
            if ($fw.ContainsKey("LocalPort")) { $params["LocalPort"] = $fw.LocalPort }
            if ($fw.ContainsKey("IcmpType")) { $params["IcmpType"] = $fw.IcmpType }
            New-NetFirewallRule @params | Out-Null
        }
    }
    Save-NetGPO -GPOSession $gpoSession
    Write-Host "Configured Domain Firewall rules inside '$gpoMgmt'." -ForegroundColor Green
} catch {
    Write-Warning "Could not save firewall rules via Open-NetGPO: $($_.Exception.Message)"
}

# 6b. GPO-Windows-LAPS (Cleartext AD backup matching OpenCMTools ActiveDirectoryClient.cs)
$gpoWinLaps = "GPO-Windows-LAPS"
Ensure-GpoLinked -GpoName $gpoWinLaps -TargetOu $WorkstationsOu | Out-Null
$lapsKey = "HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\LAPS"
Set-GPRegistryValue -Name $gpoWinLaps -Key $lapsKey -ValueName "BackupDirectory" -Type DWord -Value 2 | Out-Null
Set-GPRegistryValue -Name $gpoWinLaps -Key $lapsKey -ValueName "ADPasswordEncryptionEnabled" -Type DWord -Value 0 | Out-Null
Set-GPRegistryValue -Name $gpoWinLaps -Key $lapsKey -ValueName "PasswordComplexity" -Type DWord -Value 4 | Out-Null
Set-GPRegistryValue -Name $gpoWinLaps -Key $lapsKey -ValueName "PasswordLength" -Type DWord -Value 14 | Out-Null
Set-GPRegistryValue -Name $gpoWinLaps -Key $lapsKey -ValueName "PasswordAgeDays" -Type DWord -Value 30 | Out-Null

# 6c. GPO-Legacy-LAPS (for LAB-WS02)
$gpoLegacyLaps = "GPO-Legacy-LAPS"
Ensure-GpoLinked -GpoName $gpoLegacyLaps -TargetOu $WorkstationsOu | Out-Null
$admPwdKey = "HKLM\Software\Policies\Microsoft Services\AdmPwd"
Set-GPRegistryValue -Name $gpoLegacyLaps -Key $admPwdKey -ValueName "AdmPwdEnabled" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoLegacyLaps -Key $admPwdKey -ValueName "PasswordComplexity" -Type DWord -Value 4 | Out-Null
Set-GPRegistryValue -Name $gpoLegacyLaps -Key $admPwdKey -ValueName "PasswordLength" -Type DWord -Value 14 | Out-Null
Set-GPRegistryValue -Name $gpoLegacyLaps -Key $admPwdKey -ValueName "PasswordAgeDays" -Type DWord -Value 30 | Out-Null

# 6d. GPO-BitLocker-AD-Backup (OS + Fixed Data Drives)
$gpoBl = "GPO-BitLocker-AD-Backup"
Ensure-GpoLinked -GpoName $gpoBl -TargetOu $WorkstationsOu | Out-Null
$fveKey = "HKLM\Software\Policies\Microsoft\FVE"
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "ActiveDirectoryBackup" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "FDVActiveDirectoryBackup" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "FDVActiveDirectoryInfoToStore" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "FDVRequireActiveDirectoryBackup" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "FDVRecovery" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "FDVRecoveryPassword" -Type DWord -Value 2 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "OSActiveDirectoryBackup" -Type DWord -Value 1 | Out-Null
Set-GPRegistryValue -Name $gpoBl -Key $fveKey -ValueName "OSActiveDirectoryInfoToStore" -Type DWord -Value 1 | Out-Null

Write-Host ""
Write-Host "LAB-DC01 configuration complete!" -ForegroundColor Green
Write-Host "Manual reminders (also documented in docs/lab-setup/README.md):" -ForegroundColor Yellow
Write-Host "  1. Take a Hyper-V checkpoint 'DC-Ready' on LAB-DC01."
Write-Host "  2. After LAB-CM01 joins lab.local, run:"
Write-Host "     .\03-DC01-Configure-Domain-GPO-LAPS.ps1 -GrantCm01SystemManagementAcl"
Write-Host "  3. After LAB-WS01 and LAB-WS02 join lab.local, run:"
Write-Host "     .\03-DC01-Configure-Domain-GPO-LAPS.ps1 -ApplyGpoSecurityFiltering"
