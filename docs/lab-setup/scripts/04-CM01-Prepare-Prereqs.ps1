<#
.SYNOPSIS
    Prepares LAB-CM01 for MECM Primary Site installation and optionally creates starter MECM objects afterward.
.DESCRIPTION
    Run inside LAB-CM01 in three phases:
      1. -Phase NetworkAndJoin  : Sets static IP 10.20.30.20/24, renames to LAB-CM01, joins lab.local (OU=Servers), reboots.
      2. -Phase RolesAndDisks   : Initializes Disk 1 as E:\ (MECM-Data), places C:\NO_SMS_ON_DRIVE.SMS, creates folders,
                                  installs IIS/BITS/RDC/WSUS/RSAT prerequisites, and opens MECM/SQL firewall ports.
      3. -Phase PostMecmObjects : Run AFTER MECM Primary Site Setup finishes to auto-create the Lab Boundary, Boundary Group,
                                  'All Lab Workstations' collection, 'Lab Maintenance Window Test' collection + Maintenance Window,
                                  and a starter Package/Program for Remote Software Center testing.
.PARAMETER Phase
    Execution phase: NetworkAndJoin, RolesAndDisks, or PostMecmObjects.
.PARAMETER SiteCode
    MECM 3-character site code (default: LAB). Used in PostMecmObjects phase.
.PARAMETER DomainCredential
    Domain Admin credentials (e.g., LAB\cmadmin) for joining lab.local in NetworkAndJoin phase.
.EXAMPLE
    .\04-CM01-Prepare-Prereqs.ps1 -Phase NetworkAndJoin
.EXAMPLE
    .\04-CM01-Prepare-Prereqs.ps1 -Phase RolesAndDisks
.EXAMPLE
    .\04-CM01-Prepare-Prereqs.ps1 -Phase PostMecmObjects -SiteCode LAB
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("NetworkAndJoin", "RolesAndDisks", "PostMecmObjects")]
    [string]$Phase,

    [string]$SiteCode = "LAB",

    [PSCredential]$DomainCredential
)

$ErrorActionPreference = "Stop"

$TargetComputerName = "LAB-CM01"
$IpAddress = "10.20.30.20"
$PrefixLength = 24
$Gateway = "10.20.30.1"
$DnsServer = "10.20.30.10"
$DomainFqdn = "lab.local"
$ServersOu = "OU=Servers,DC=lab,DC=local"

if ($Phase -eq "NetworkAndJoin") {
    Write-Host "=== Phase 1: Configuring Static IP ($IpAddress/$PrefixLength) & Domain Join ===" -ForegroundColor Cyan

    $adapter = Get-NetAdapter | Where-Object { $_.Status -eq "Up" } | Select-Object -First 1
    if (-not $adapter) {
        throw "No active network adapter found on LAB-CM01."
    }

    $existingIp = Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -eq $IpAddress }
    if (-not $existingIp) {
        Set-NetIPInterface -InterfaceIndex $adapter.ifIndex -Dhcp Disabled -ErrorAction SilentlyContinue
        Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
        Get-NetRoute -InterfaceIndex $adapter.ifIndex -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
            Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue

        New-NetIPAddress -InterfaceIndex $adapter.ifIndex `
            -IPAddress $IpAddress `
            -PrefixLength $PrefixLength `
            -DefaultGateway $Gateway | Out-Null
    }
    Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses @($DnsServer)
    Write-Host "Configured IP $IpAddress/$PrefixLength, GW $Gateway, DNS $DnsServer." -ForegroundColor Green

    if (-not (Test-Connection -ComputerName $DnsServer -Count 2 -Quiet)) {
        throw "Cannot reach LAB-DC01 ($DnsServer). Verify LAB-DC01 is running and on OpenCMTools-LabNet."
    }

    $cs = Get-CimInstance Win32_ComputerSystem
    if ($cs.PartOfDomain -and $cs.Domain -eq $DomainFqdn -and $env:COMPUTERNAME -eq $TargetComputerName) {
        Write-Host "LAB-CM01 is already joined to $DomainFqdn." -ForegroundColor DarkGray
        return
    }

    if (-not $DomainCredential) {
        $DomainCredential = Get-Credential -UserName "LAB\cmadmin" -Message "Enter Domain Admin credentials to join LAB-CM01 to $DomainFqdn"
    }

    Write-Host "Joining $DomainFqdn as $TargetComputerName in '$ServersOu' and rebooting..." -ForegroundColor Yellow
    Add-Computer -DomainName $DomainFqdn -NewName $TargetComputerName -OUPath $ServersOu -Credential $DomainCredential -Restart -Force
    return
}

if ($Phase -eq "RolesAndDisks") {
    Write-Host "=== Phase 2A: Initializing E:\ Data Disk & Folder Layout ===" -ForegroundColor Cyan

    if (-not (Test-Path "E:\")) {
        $rawDisk = Get-Disk | Where-Object { $_.PartitionStyle -eq "RAW" -and $_.Number -gt 0 } | Select-Object -First 1
        if ($rawDisk) {
            $rawDisk | Initialize-Disk -PartitionStyle GPT -PassThru |
                New-Partition -DriveLetter E -UseMaximumSize |
                Format-Volume -FileSystem NTFS -NewFileSystemLabel "MECM-Data" -Confirm:$false | Out-Null
            Write-Host "Initialized and formatted E:\ (MECM-Data)." -ForegroundColor Green
        } else {
            Write-Warning "No RAW secondary disk found for E:\. If you want a separate data drive, attach a 120 GB VHDX first."
        }
    } else {
        Write-Host "Drive E:\ already exists." -ForegroundColor DarkGray
    }

    # Prevent MECM from placing content library / site components on C:\
    if (-not (Test-Path "C:\NO_SMS_ON_DRIVE.SMS")) {
        New-Item -Path "C:\NO_SMS_ON_DRIVE.SMS" -ItemType File -Force | Out-Null
        Write-Host "Created C:\NO_SMS_ON_DRIVE.SMS so ConfigMgr uses E:\ for data/content." -ForegroundColor Green
    }

    $dataRoot = if (Test-Path "E:\") { "E:" } else { "C:" }
    $folders = @(
        "$dataRoot\SQLData",
        "$dataRoot\SQLLogs",
        "$dataRoot\WSUS",
        "$dataRoot\CMPrereqs",
        "$dataRoot\Sources\Apps",
        "$dataRoot\Sources\Packages",
        "$dataRoot\Sources\Updates",
        "C:\Tools",
        "C:\Install"
    )
    foreach ($f in $folders) {
        New-Item -Path $f -ItemType Directory -Force | Out-Null
    }
    Write-Host "Created standard lab directories under $dataRoot\ and C:\." -ForegroundColor Green

    Write-Host "=== Phase 2B: Installing MECM Primary Site, MP, DP & WSUS Prerequisites ===" -ForegroundColor Cyan
    $features = @(
        "Web-Server",
        "Web-WebServer",
        "Web-Common-Http",
        "Web-Default-Doc",
        "Web-Dir-Browsing",
        "Web-Http-Errors",
        "Web-Static-Content",
        "Web-Http-Redirect",
        "Web-Health",
        "Web-Http-Logging",
        "Web-Log-Libraries",
        "Web-Request-Monitor",
        "Web-Http-Tracing",
        "Web-Performance",
        "Web-Stat-Compression",
        "Web-Dyn-Compression",
        "Web-Security",
        "Web-Filtering",
        "Web-Windows-Auth",
        "Web-App-Dev",
        "Web-Net-Ext",
        "Web-Net-Ext45",
        "Web-Asp-Net",
        "Web-Asp-Net45",
        "Web-ISAPI-Ext",
        "Web-ISAPI-Filter",
        "Web-Mgmt-Tools",
        "Web-Mgmt-Console",
        "Web-Mgmt-Compat",
        "Web-Metabase",
        "Web-WMI",
        "Web-Scripting-Tools",
        "BITS",
        "BITS-IIS-Ext",
        "RDC",
        "NET-Framework-45-Features",
        "NET-Framework-45-Core",
        "NET-Framework-45-ASPNET",
        "NET-WCF-HTTP-Activation45",
        "UpdateServices-Services",
        "UpdateServices-DB",
        "RSAT-AD-PowerShell"
    )

    Install-WindowsFeature -Name $features -IncludeManagementTools | Out-Null
    Write-Host "Installed IIS, BITS, RDC, .NET 4.x, WSUS (SQL mode), and AD PowerShell tools." -ForegroundColor Green

    # Optional .NET 3.5 (required only if older tools/components request it)
    try {
        Install-WindowsFeature -Name "NET-Framework-Core" -ErrorAction Stop | Out-Null
        Write-Host "Installed .NET Framework 3.5." -ForegroundColor Green
    } catch {
        Write-Warning "Skipped optional .NET 3.5 (Windows Update / SxS media required if needed later): $($_.Exception.Message)"
    }

    Write-Host "=== Phase 2C: Opening Local Firewall Ports for SQL, MECM, and WSUS ===" -ForegroundColor Cyan
    $fwRules = @(
        @{ Name = "OpenCMTools-CM-SQL"; DisplayName = "MECM Lab - SQL Server (TCP 1433)"; Port = "1433" },
        @{ Name = "OpenCMTools-CM-SQLBroker"; DisplayName = "MECM Lab - SQL Service Broker (TCP 4022)"; Port = "4022" },
        @{ Name = "OpenCMTools-CM-HTTP"; DisplayName = "MECM Lab - MP/DP HTTP (TCP 80)"; Port = "80" },
        @{ Name = "OpenCMTools-CM-HTTPS"; DisplayName = "MECM Lab - MP/DP HTTPS/eHTTP (TCP 443)"; Port = "443" },
        @{ Name = "OpenCMTools-CM-WSUS-HTTP"; DisplayName = "MECM Lab - WSUS HTTP (TCP 8530)"; Port = "8530" },
        @{ Name = "OpenCMTools-CM-WSUS-HTTPS"; DisplayName = "MECM Lab - WSUS HTTPS (TCP 8531)"; Port = "8531" },
        @{ Name = "OpenCMTools-CM-FastChannel"; DisplayName = "MECM Lab - Client Notification Fast Channel (TCP 10123)"; Port = "10123" }
    )
    foreach ($r in $fwRules) {
        if (-not (Get-NetFirewallRule -Name $r.Name -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -Name $r.Name -DisplayName $r.DisplayName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $r.Port -Profile Domain | Out-Null
        }
    }
    Write-Host "Configured Domain Firewall rules on LAB-CM01." -ForegroundColor Green

    Write-Host ""
    Write-Host "LAB-CM01 OS prerequisites complete!" -ForegroundColor Green
    Write-Host "Next steps (see docs/lab-setup/README.md Phase 2):" -ForegroundColor Yellow
    Write-Host "  1. On LAB-DC01, run: .\03-DC01-Configure-Domain-GPO-LAPS.ps1 -GrantCm01SystemManagementAcl"
    Write-Host "  2. Install SQL Server 2022/2025 Eval (Collation: SQL_Latin1_General_CP1_CI_AS, Max Memory: 4096 MB) + SSMS + ODBC Driver 18."
    Write-Host "  3. Install Windows ADK + WinPE Add-on."
    Write-Host "  4. Run WSUS post-install against SQL Server, extend AD schema (extadsch.exe), and install MECM Primary Site (Site Code: $SiteCode)."
    Write-Host "  5. After MECM finishes, run: .\04-CM01-Prepare-Prereqs.ps1 -Phase PostMecmObjects -SiteCode $SiteCode"
    return
}

if ($Phase -eq "PostMecmObjects") {
    Write-Host "=== Phase 3: Creating Starter MECM Boundaries, Collections, Maintenance Window & Test Package ===" -ForegroundColor Cyan

    $uiPath = $env:SMS_ADMIN_UI_PATH
    if ([string]::IsNullOrWhiteSpace($uiPath)) {
        $uiPath = "C:\Program Files (x86)\Microsoft Endpoint Manager\AdminConsole\bin\i386"
    }
    $cmModule = Join-Path (Split-Path $uiPath -Parent) "ConfigurationManager.psd1"
    if (-not (Test-Path $cmModule)) {
        throw "ConfigurationManager PowerShell module not found at '$cmModule'. Ensure MECM Admin Console is installed and reopen PowerShell."
    }

    Import-Module $cmModule
    $siteDrive = "${SiteCode}:"
    if (-not (Get-PSDrive -Name $SiteCode -PSProvider CMSite -ErrorAction SilentlyContinue)) {
        New-PSDrive -Name $SiteCode -PSProvider CMSite -Root "LAB-CM01.lab.local" | Out-Null
    }
    Push-Location $siteDrive

    try {
        # 1. IP Subnet Boundary & Boundary Group
        $boundaryName = "OpenCMTools Lab Subnet (10.20.30.0/24)"
        if (-not (Get-CMBoundary -BoundaryName $boundaryName -ErrorAction SilentlyContinue)) {
            New-CMBoundary -DisplayName $boundaryName -BoundaryType IPSubnet -Value "10.20.30.0" | Out-Null
            Write-Host "  Created IP Subnet Boundary: $boundaryName" -ForegroundColor Gray
        }

        $bgName = "OpenCMTools Lab Primary BG"
        if (-not (Get-CMBoundaryGroup -Name $bgName -ErrorAction SilentlyContinue)) {
            New-CMBoundaryGroup -Name $bgName -DefaultSiteCode $SiteCode | Out-Null
            Write-Host "  Created Boundary Group: $bgName" -ForegroundColor Gray
        }
        Add-CMBoundaryToGroup -BoundaryName $boundaryName -BoundaryGroupName $bgName -ErrorAction SilentlyContinue
        Set-CMBoundaryGroup -Name $bgName -AddSiteSystemServerName "LAB-CM01.lab.local" -ErrorAction SilentlyContinue

        # 2. Device Collection: All Lab Workstations (query-based)
        $wsCol = "All Lab Workstations"
        if (-not (Get-CMDeviceCollection -Name $wsCol -ErrorAction SilentlyContinue)) {
            $sched = New-CMSchedule -RecurInterval Minutes -RecurCount 15
            New-CMDeviceCollection -Name $wsCol -LimitingCollectionName "All Systems" -RefreshSchedule $sched -RefreshType Periodic | Out-Null
            Add-CMDeviceCollectionQueryMembershipRule `
                -CollectionName $wsCol `
                -RuleName "LAB-WS Workstations" `
                -QueryExpression "select SMS_R_SYSTEM.ResourceID,SMS_R_SYSTEM.ResourceType,SMS_R_SYSTEM.Name,SMS_R_SYSTEM.SMSUniqueIdentifier,SMS_R_SYSTEM.ResourceDomainORWorkgroup,SMS_R_SYSTEM.Client from SMS_R_System where SMS_R_System.Name like 'LAB-WS%'"
            Write-Host "  Created Device Collection: $wsCol" -ForegroundColor Gray
        }

        # 3. Device Collection with Maintenance Window (for OpenCMTools 'Show Collections - Advanced')
        $mwCol = "Lab Maintenance Window Test"
        if (-not (Get-CMDeviceCollection -Name $mwCol -ErrorAction SilentlyContinue)) {
            New-CMDeviceCollection -Name $mwCol -LimitingCollectionName $wsCol | Out-Null
            $mwSched = New-CMSchedule -RecurInterval Days -RecurCount 7 -Start (Get-Date "02:00") -DurationInterval Hours -DurationCount 2
            New-CMMaintenanceWindow -CollectionName $mwCol -Name "Weekly Lab Software Updates Window" -Schedule $mwSched -ApplyTo SoftwareUpdatesOnly | Out-Null
            Write-Host "  Created Device Collection + Maintenance Window: $mwCol" -ForegroundColor Gray
        }

        # Add LAB-WS01 to the Maintenance Window collection if discovered already
        $ws1Res = Get-CMDevice -Name "LAB-WS01" -ErrorAction SilentlyContinue
        if ($ws1Res) {
            Add-CMDeviceCollectionDirectMembershipRule -CollectionName $mwCol -ResourceId $ws1Res.ResourceID -ErrorAction SilentlyContinue
            Write-Host "  Added LAB-WS01 to '$mwCol'." -ForegroundColor Gray
        }

        # 4. Starter Package & Program (populates Remote Software Center -> Packages tab)
        $pkgName = "OpenCMTools Test Package"
        $pkg = Get-CMPackage -Name $pkgName -Fast -ErrorAction SilentlyContinue
        if (-not $pkg) {
            $pkg = New-CMPackage -Name $pkgName -Description "Lightweight test package for OpenCMTools Remote Software Center"
            New-CMProgram `
                -PackageName $pkgName `
                -StandardProgramName "Echo OpenCMTools Test" `
                -CommandLine "cmd.exe /c echo OpenCMTools Remote Package Test OK" `
                -RunMode RunWithAdministrativeRights `
                -ProgramRunType WhetherOrNotUserIsLoggedOn | Out-Null
            Start-CMPackageDeployment `
                -PackageName $pkgName `
                -ProgramName "Echo OpenCMTools Test" `
                -CollectionName $wsCol `
                -DeployPurpose Available `
                -StandardProgram `
                -AllowUsersRunIndependently $true | Out-Null
            Write-Host "  Created & deployed starter Package: $pkgName -> $wsCol" -ForegroundColor Gray
        }

        Write-Host ""
        Write-Host "Starter MECM objects created successfully!" -ForegroundColor Green
    } finally {
        Pop-Location
    }
}
