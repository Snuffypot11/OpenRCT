<#
.SYNOPSIS
    Configures LAB-DC01 networking/hostname, installs AD DS, DNS, DHCP, GPMC, and promotes to lab.local.
.DESCRIPTION
    Run inside LAB-DC01 right after installing Windows Server 2022/2025.
    - First run: sets static IP (10.20.30.10/24), renames the computer to LAB-DC01, and reboots.
    - Second run (after reboot): installs AD DS, DNS, DHCP, and GPMC roles and promotes LAB-DC01
      as the forest root Domain Controller for 'lab.local' (reboots automatically upon completion).
.PARAMETER SafeModePassword
    Directory Services Restore Mode (DSRM) Administrator password (prompted if omitted).
.EXAMPLE
    .\02-DC01-Install-AD-DNS-DHCP.ps1
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [SecureString]$SafeModePassword
)

$ErrorActionPreference = "Stop"

$TargetComputerName = "LAB-DC01"
$IpAddress = "10.20.30.10"
$PrefixLength = 24
$Gateway = "10.20.30.1"
$DomainFqdn = "lab.local"
$DomainNetbios = "LAB"

Write-Host "=== Step 1: Configuring Static IP ($IpAddress/$PrefixLength) ===" -ForegroundColor Cyan

$adapter = Get-NetAdapter | Where-Object { $_.Status -eq "Up" } | Select-Object -First 1
if (-not $adapter) {
    throw "No active network adapter found. Ensure LAB-DC01 is connected to the 'OpenRCT-LabNet' vSwitch."
}

$currentIp = Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -eq $IpAddress }

if (-not $currentIp) {
    Set-NetIPInterface -InterfaceIndex $adapter.ifIndex -Dhcp Disabled -ErrorAction SilentlyContinue
    Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
    Get-NetRoute -InterfaceIndex $adapter.ifIndex -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
        Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue

    New-NetIPAddress -InterfaceIndex $adapter.ifIndex `
        -IPAddress $IpAddress `
        -PrefixLength $PrefixLength `
        -DefaultGateway $Gateway | Out-Null

    Write-Host "Configured static IP $IpAddress/$PrefixLength with gateway $Gateway." -ForegroundColor Green
} else {
    Write-Host "Static IP $IpAddress already configured." -ForegroundColor DarkGray
}

# Point DNS temporarily to public resolver before AD promotion (AD promotion will update to 127.0.0.1)
Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses @("127.0.0.1", "1.1.1.1")

if ($env:COMPUTERNAME -ne $TargetComputerName) {
    Write-Host "Renaming server from '$env:COMPUTERNAME' to '$TargetComputerName'..." -ForegroundColor Cyan
    Rename-Computer -NewName $TargetComputerName -Force
    Write-Host "Rebooting now so the hostname change takes effect. After reboot, re-run this exact script to promote to DC." -ForegroundColor Yellow
    Restart-Computer -Force
    return
}

Write-Host "=== Step 2: Installing AD DS, DNS, DHCP, and GPMC Roles ===" -ForegroundColor Cyan
$features = @("AD-Domain-Services", "DNS", "DHCP", "GPMC", "RSAT-AD-PowerShell", "RSAT-AD-AdminCenter")
Install-WindowsFeature -Name $features -IncludeManagementTools | Out-Null
Write-Host "Server roles installed." -ForegroundColor Green

Write-Host "=== Step 3: Promoting LAB-DC01 to Forest Root DC ($DomainFqdn) ===" -ForegroundColor Cyan
if (-not $SafeModePassword) {
    $SafeModePassword = Read-Host -Prompt "Enter Directory Services Restore Mode (DSRM) Password" -AsSecureString
}

Import-Module ADDSDeployment
Install-ADDSForest `
    -DomainName $DomainFqdn `
    -DomainNetbiosName $DomainNetbios `
    -ForestMode WinThreshold `
    -DomainMode WinThreshold `
    -InstallDns:$true `
    -CreateDnsDelegation:$false `
    -DatabasePath "C:\Windows\NTDS" `
    -LogPath "C:\Windows\NTDS" `
    -SysvolPath "C:\Windows\SYSVOL" `
    -SafeModeAdministratorPassword $SafeModePassword `
    -NoRebootOnCompletion:$false `
    -Force:$true
