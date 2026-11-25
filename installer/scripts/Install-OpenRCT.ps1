<#
.SYNOPSIS
    Installs OpenRCT (Open Source Right Click Tools for Microsoft Configuration Manager).
.DESCRIPTION
    Auto-detects the MECM Admin Console installation path, installs OpenRCT binaries to Program Files,
    binds OpenRCT.ConsoleExtension.dll against the local MECM Console assemblies (Assembly mode) or
    configures Named-Pipe Executable fallback mode, and deploys XML extensions for Devices,
    Device Collections, and Console Dashboards.
.PARAMETER InstallDir
    Target installation folder for OpenRCT binaries. Defaults to "$env:ProgramFiles\OpenRCT".
.PARAMETER ConsoleRoot
    Optional explicit path to the MECM AdminConsole folder. Auto-detected if omitted.
.PARAMETER Mode
    'Assembly' (default, native multi-select + embedded console dashboards) or 'Executable' (Named-Pipe fallback).
.PARAMETER PsExecPath
    Optional path to Sysinternals PsExec.exe or PsExec64.exe to pre-populate in user settings.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\OpenRCT",
    [string]$ConsoleRoot = "",
    [ValidateSet("Assembly", "Executable")]
    [string]$Mode = "Assembly",
    [string]$PsExecPath = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-MecmConsoleRoot {
    param([string]$ExplicitPath)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path $ExplicitPath)) {
        return (Resolve-Path $ExplicitPath).Path.TrimEnd('\')
    }

    if (-not [string]::IsNullOrWhiteSpace($env:SMS_ADMIN_UI_PATH)) {
        $candidate = Split-Path -Path $env:SMS_ADMIN_UI_PATH -Parent
        if (Test-Path (Join-Path $candidate "XmlStorage")) {
            return $candidate.TrimEnd('\')
        }
    }

    $regKeys = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\ConfigMgr10\Setup",
        "HKLM:\SOFTWARE\Microsoft\ConfigMgr10\Setup"
    )
    foreach ($rk in $regKeys) {
        if (Test-Path $rk) {
            $uiDir = (Get-ItemProperty -Path $rk -Name "UI Installation Directory" -ErrorAction SilentlyContinue)."UI Installation Directory"
            if (-not [string]::IsNullOrWhiteSpace($uiDir) -and (Test-Path (Join-Path $uiDir "XmlStorage"))) {
                return $uiDir.TrimEnd('\')
            }
        }
    }

    $commonPaths = @(
        "${env:ProgramFiles(x86)}\Microsoft Endpoint Manager\AdminConsole",
        "${env:ProgramFiles(x86)}\Microsoft Configuration Manager\AdminConsole",
        "$env:ProgramFiles\Microsoft Endpoint Manager\AdminConsole"
    )
    foreach ($p in $commonPaths) {
        if (Test-Path (Join-Path $p "XmlStorage")) { return $p }
    }

    return $null
}

function Resolve-AssetsOverviewNodeGuid {
    param([string]$AdminConsoleRoot)
    $defaultGuid = "c1a5195c-6c5f-4236-a9b8-5e27e7f91470"
    $assetXmlPath = Join-Path $AdminConsoleRoot "XmlStorage\ConsoleRoot\AssetManagementNode.xml"
    if (-not (Test-Path $assetXmlPath)) { return $defaultGuid }

    try {
        [xml]$doc = Get-Content -Path $assetXmlPath -Raw
        $overviewNode = $doc.SelectSingleNode("//RootNodeDescription[@NamespaceGuid]")
        if ($overviewNode -and -not [string]::IsNullOrWhiteSpace($overviewNode.NamespaceGuid)) {
            return $overviewNode.NamespaceGuid
        }
    } catch {
        Write-Warning "Could not parse AssetManagementNode.xml; using default Overview GUID $defaultGuid."
    }
    return $defaultGuid
}

function Build-LocalConsoleExtension {
    param(
        [string]$AdminConsoleBin,
        [string]$TargetDir,
        [string]$BridgeSourceDir
    )
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) {
        $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
    }
    if (-not (Test-Path $csc)) {
        Write-Warning "csc.exe not found; using prebuilt OpenRCT.ConsoleExtension.dll."
        return
    }

    $requiredSdkDlls = @(
        (Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.dll"),
        (Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.ManagementProvider.dll"),
        (Join-Path $AdminConsoleBin "AdminUI.ConsoleView.dll")
    )
    foreach ($dll in $requiredSdkDlls) {
        if (-not (Test-Path $dll)) {
            Write-Warning "MECM Console assembly not found ($dll); skipping local bridge binding."
            return
        }
    }

    $srcFiles = Get-ChildItem -Path $BridgeSourceDir -Filter "*.cs" -ErrorAction SilentlyContinue
    if (-not $srcFiles -or $srcFiles.Count -eq 0) { return }

    $outDll = Join-Path $TargetDir "OpenRCT.ConsoleExtension.dll"
    $wpfRefDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
    $argsList = @(
        "/target:library",
        "/nologo",
        "/optimize+",
        "/out:`"$outDll`"",
        "/r:`"$(Join-Path $TargetDir 'OpenRCT.Core.dll')`"",
        "/r:`"$(Join-Path $TargetDir 'OpenRCT.UI.dll')`"",
        "/r:`"System.Windows.Forms.dll`"",
        "/r:`"$(Join-Path $wpfRefDir 'PresentationCore.dll')`"",
        "/r:`"$(Join-Path $wpfRefDir 'PresentationFramework.dll')`"",
        "/r:`"$(Join-Path $wpfRefDir 'WindowsBase.dll')`"",
        "/r:`"$(Join-Path $wpfRefDir 'WindowsFormsIntegration.dll')`""
    )
    foreach ($dll in $requiredSdkDlls) { $argsList += "/r:`"$dll`"" }
    foreach ($sf in $srcFiles) { $argsList += "`"$($sf.FullName)`"" }

    Write-Host "Binding OpenRCT.ConsoleExtension.dll to installed MECM Console assemblies..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $csc -ArgumentList ($argsList -join " ") -Wait -PassThru -NoNewWindow
    if ($proc.ExitCode -ne 0) {
        Write-Warning "Local console extension binding returned exit code $($proc.ExitCode). Falling back to existing binary."
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Install-OpenRCT.ps1 must be run from an elevated (Administrator) PowerShell session."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Split-Path -Parent $scriptDir
$binSource = Join-Path $packageRoot "bin"
if (-not (Test-Path $binSource)) { $binSource = $scriptDir }

Write-Host "Installing OpenRCT to '$InstallDir'..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $binSource "*") -Destination $InstallDir -Recurse -Force
Get-ChildItem -Path $InstallDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

$detectedConsole = Find-MecmConsoleRoot -ExplicitPath $ConsoleRoot
if (-not $detectedConsole) {
    Write-Warning "MECM Admin Console was not detected. OpenRCT standalone launcher was installed to '$InstallDir', but console XML extensions were skipped."
} else {
    Write-Host "Detected MECM Admin Console: $detectedConsole" -ForegroundColor Green
    $consoleBin = Join-Path $detectedConsole "bin"
    $bridgeSource = Join-Path $packageRoot "console-bridge-src"
    if ($Mode -eq "Assembly" -and (Test-Path $bridgeSource)) {
        Build-LocalConsoleExtension -AdminConsoleBin $consoleBin -TargetDir $InstallDir -BridgeSourceDir $bridgeSource
    }

    $xmlTemplatesRoot = Join-Path $packageRoot "console-extensions\XmlStorage\Extensions"
    if (-not (Test-Path $xmlTemplatesRoot)) {
        $xmlTemplatesRoot = Join-Path (Split-Path -Parent $packageRoot) "console-extensions\XmlStorage\Extensions"
    }

    $deviceActionTemplate = if ($Mode -eq "Assembly") {
        Join-Path $xmlTemplatesRoot "Actions\OpenRCT_DeviceActions.xml"
    } else {
        Join-Path $xmlTemplatesRoot "Actions\OpenRCT_DeviceActions_Executable.xml"
    }
    $collectionActionTemplate = Join-Path $xmlTemplatesRoot "Actions\OpenRCT_CollectionActions.xml"
    $nodesTemplate = Join-Path $xmlTemplatesRoot "Nodes\OpenRCT_Nodes.xml"

    $deviceGuids = @(
        "ed9dee86-eadd-4ac8-82a1-7234a4646e62", # All Devices
        "3fd01cd1-9e01-461e-92cd-94866b8d1f39"  # Devices in Collection
    )
    foreach ($guid in $deviceGuids) {
        $destDir = Join-Path $detectedConsole "XmlStorage\Extensions\Actions\$guid"
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        $xml = (Get-Content -Path $deviceActionTemplate -Raw).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
        Set-Content -Path (Join-Path $destDir "OpenRCT_DeviceActions.xml") -Value $xml -Encoding UTF8
    }

    if ($Mode -eq "Assembly" -and (Test-Path $collectionActionTemplate)) {
        $colDestDir = Join-Path $detectedConsole "XmlStorage\Extensions\Actions\a92615d6-9df3-49ba-a8c9-6ecb0e8b956b"
        New-Item -ItemType Directory -Path $colDestDir -Force | Out-Null
        $colXml = (Get-Content -Path $collectionActionTemplate -Raw).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
        Set-Content -Path (Join-Path $colDestDir "OpenRCT_CollectionActions.xml") -Value $colXml -Encoding UTF8
    }

    if ($Mode -eq "Assembly" -and (Test-Path $nodesTemplate)) {
        $overviewGuid = Resolve-AssetsOverviewNodeGuid -AdminConsoleRoot $detectedConsole
        $nodeDestDir = Join-Path $detectedConsole "XmlStorage\Extensions\Nodes\$overviewGuid"
        New-Item -ItemType Directory -Path $nodeDestDir -Force | Out-Null
        $nodeXml = (Get-Content -Path $nodesTemplate -Raw).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
        Set-Content -Path (Join-Path $nodeDestDir "OpenRCT_Nodes.xml") -Value $nodeXml -Encoding UTF8
    }
}

# Create Start Menu shortcut for OpenRCT Standalone / Settings
$startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\OpenRCT"
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "OpenRCT.lnk"))
$shortcut.TargetPath = Join-Path $InstallDir "OpenRCT.App.exe"
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = "OpenRCT — Open Source Right Click Tools for MECM"
$shortcut.Save()

if (-not [string]::IsNullOrWhiteSpace($PsExecPath)) {
    $appDataDir = Join-Path $env:APPDATA "OpenRCT"
    New-Item -ItemType Directory -Path $appDataDir -Force | Out-Null
    $settingsFile = Join-Path $appDataDir "settings.json"
    if (-not (Test-Path $settingsFile)) {
        $escaped = $PsExecPath.Replace('\', '\\')
        Set-Content -Path $settingsFile -Value "{`n  `"PsExecPath`": `"$escaped`"`n}" -Encoding UTF8
    }
}

Write-Host "OpenRCT installation complete. Restart any open Configuration Manager Console to load extensions." -ForegroundColor Green
