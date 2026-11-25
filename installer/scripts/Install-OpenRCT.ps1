<#
.SYNOPSIS
    Installs and registers OpenRCT (Open Source Right Click Tools for Microsoft Configuration Manager).
.DESCRIPTION
    Auto-detects the MECM Admin Console installation path, installs OpenRCT binaries (unless -SkipBinaryCopy
    is passed from MSI), binds OpenRCT.ConsoleExtension.dll directly against the workstation's installed
    MECM Console assemblies using .NET 4.8 csc.exe, and deploys XML extensions for Devices, Device Collections,
    and Console Dashboards. Writes a full diagnostic log to C:\ProgramData\OpenRCT\install.log.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\OpenRCT",
    [string]$ConsoleRoot = "",
    [ValidateSet("Assembly", "Executable")]
    [string]$Mode = "Assembly",
    [string]$PsExecPath = "",
    [switch]$SkipBinaryCopy
)

$ErrorActionPreference = "Stop"

# Normalize paths (safely handles MSI trailing-backslash workaround like "C:\Program Files\OpenRCT\.")
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir.Trim().Trim('"')).TrimEnd('\')
if (-not [string]::IsNullOrWhiteSpace($ConsoleRoot)) {
    $ConsoleRoot = [System.IO.Path]::GetFullPath($ConsoleRoot.Trim().Trim('"')).TrimEnd('\')
}
$PsExecPath = if ($PsExecPath) { $PsExecPath.Trim().Trim('"') } else { "" }

# Diagnostic log under C:\ProgramData\OpenRCT\install.log
$logDir = Join-Path $env:ProgramData "OpenRCT"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$script:LogFile = Join-Path $logDir "install.log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    Add-Content -Path $script:LogFile -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($Level -eq "WARN") { Write-Warning $Message }
    elseif ($Level -eq "ERROR") { Write-Host $Message -ForegroundColor Red }
    else { Write-Host $Message -ForegroundColor Cyan }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-ConsoleRootFromCandidate {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $current = $Path.Trim().Trim('"').TrimEnd('\')
    for ($i = 0; $i -lt 5; $i++) {
        if ([string]::IsNullOrWhiteSpace($current)) { break }
        if ((Test-Path (Join-Path $current "XmlStorage")) -and (Test-Path (Join-Path $current "bin"))) {
            return $current
        }
        $parent = Split-Path -Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
    return $null
}

function Find-MecmConsoleRoot {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = Resolve-ConsoleRootFromCandidate -Path $ExplicitPath
        if ($resolved) { return $resolved }
    }

    # 1. Environment variables (both process and Machine scope - handles ...\AdminConsole\bin\i386)
    $envCandidates = @(
        $env:SMS_ADMIN_UI_PATH,
        [Environment]::GetEnvironmentVariable("SMS_ADMIN_UI_PATH", "Machine"),
        [Environment]::GetEnvironmentVariable("SMS_ADMIN_UI_PATH", "User")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($cand in $envCandidates) {
        Write-Log "Checking SMS_ADMIN_UI_PATH candidate: $cand"
        $resolved = Resolve-ConsoleRootFromCandidate -Path $cand
        if ($resolved) { return $resolved }
    }

    # 2. Registry keys (32-bit and 64-bit views)
    $regKeys = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\ConfigMgr10\Setup",
        "HKLM:\SOFTWARE\Microsoft\ConfigMgr10\Setup",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\SMS\Setup",
        "HKLM:\SOFTWARE\Microsoft\SMS\Setup"
    )
    foreach ($rk in $regKeys) {
        if (Test-Path $rk) {
            $props = Get-ItemProperty -Path $rk -ErrorAction SilentlyContinue
            foreach ($valName in @("UI Installation Directory", "Installation Directory")) {
                $val = $props.$valName
                if (-not [string]::IsNullOrWhiteSpace($val)) {
                    Write-Log "Checking registry $rk\$valName = $val"
                    $resolved = Resolve-ConsoleRootFromCandidate -Path $val
                    if ($resolved) { return $resolved }
                    $adminSub = Resolve-ConsoleRootFromCandidate -Path (Join-Path $val "AdminConsole")
                    if ($adminSub) { return $adminSub }
                }
            }
        }
    }

    # 3. Common installation directories
    $commonPaths = @(
        "${env:ProgramFiles(x86)}\Microsoft Endpoint Manager\AdminConsole",
        "${env:ProgramFiles(x86)}\Microsoft Configuration Manager\AdminConsole",
        "$env:ProgramFiles\Microsoft Endpoint Manager\AdminConsole",
        "$env:ProgramFiles\Microsoft Configuration Manager\AdminConsole",
        "D:\Program Files\Microsoft Configuration Manager\AdminConsole",
        "D:\Program Files (x86)\Microsoft Configuration Manager\AdminConsole",
        "E:\Program Files\Microsoft Configuration Manager\AdminConsole"
    )
    foreach ($p in $commonPaths) {
        $resolved = Resolve-ConsoleRootFromCandidate -Path $p
        if ($resolved) { return $resolved }
    }

    return $null
}

function Resolve-AssetsOverviewNodeGuid {
    param([string]$AdminConsoleRoot)
    $defaultGuid = "c1a5195c-6c5f-4236-a9b8-5e27e7f91470"
    $assetXmlPath = Join-Path $AdminConsoleRoot "XmlStorage\ConsoleRoot\AssetManagementNode.xml"
    if (-not (Test-Path $assetXmlPath)) {
        Write-Log "AssetManagementNode.xml not found at $assetXmlPath; using default GUID $defaultGuid" "WARN"
        return $defaultGuid
    }

    try {
        [xml]$doc = Get-Content -Path $assetXmlPath -Raw
        $allNodes = $doc.SelectNodes("//RootNodeDescription[@NamespaceGuid]")
        foreach ($node in $allNodes) {
            # In AssetManagementNode.xml, the Overview node is the parent of Users/Devices/Device Collections
            $childXml = $node.InnerXml
            if ($node.Id -match "Overview" -or ($childXml -match "Devices" -and $childXml -match "DeviceCollection" -and $node.ParentNode.Name -eq "ChildNodes")) {
                Write-Log "Resolved Assets and Compliance Overview node GUID from AssetManagementNode.xml: $($node.NamespaceGuid) (Id=$($node.Id))"
                return $node.NamespaceGuid
            }
        }
        if ($allNodes.Count -gt 1) {
            Write-Log "Using second RootNodeDescription GUID from AssetManagementNode.xml: $($allNodes[1].NamespaceGuid)"
            return $allNodes[1].NamespaceGuid
        }
        if ($allNodes.Count -eq 1) {
            return $allNodes[0].NamespaceGuid
        }
    } catch {
        Write-Log "Failed to parse AssetManagementNode.xml: $($_.Exception.Message). Using default $defaultGuid." "WARN"
    }
    return $defaultGuid
}

function Find-TypeNamespaceInBin {
    param([string]$AdminConsoleBin, [string]$TypeName)
    $candidates = @()
    $cmExe = Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.exe"
    if (Test-Path $cmExe) { $candidates += $cmExe }
    $candidates += (Get-ChildItem -Path $AdminConsoleBin -Filter "AdminUI*.dll" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $candidates += (Get-ChildItem -Path $AdminConsoleBin -Filter "Microsoft.ConfigurationManagement*.dll" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)

    foreach ($asmPath in ($candidates | Select-Object -Unique)) {
        try {
            $asm = $null
            try { $asm = [System.Reflection.Assembly]::LoadFrom($asmPath) } catch { }
            if ($null -eq $asm) {
                try { $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($asmPath) } catch { }
            }
            if ($null -eq $asm) { continue }
            $types = @()
            try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
            foreach ($t in $types) {
                if ($t.Name -eq $TypeName) {
                    return @{ Namespace = $t.Namespace; Assembly = $asmPath }
                }
            }
        } catch {
            # Ignore unreadable assembly
        }
    }
    return $null
}

function Invoke-CscCompile {
    param([string]$CscPath, [string[]]$ArgsList)
    $argString = $ArgsList -join " "
    Write-Log "Running csc.exe: $CscPath $argString"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $CscPath
    $psi.Arguments = $argString
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Log "csc stdout: $stdout" }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Log "csc stderr: $stderr" "WARN" }
    return $proc.ExitCode
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
        Write-Log "csc.exe not found; using prebuilt OpenRCT.ConsoleExtension.dll." "WARN"
        return $false
    }

    $cmExe = Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.exe"
    $mgmtProvider = Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.ManagementProvider.dll"
    if (-not (Test-Path $cmExe) -or -not (Test-Path $mgmtProvider)) {
        Write-Log "Required MECM Console assemblies missing in $AdminConsoleBin (checked Microsoft.ConfigurationManagement.exe and Microsoft.ConfigurationManagement.ManagementProvider.dll)." "WARN"
        return $false
    }

    $handlerCs = Join-Path $BridgeSourceDir "OpenRctActionHandler.cs"
    $viewsCs = Join-Path $BridgeSourceDir "DashboardConsoleViews.cs"
    if (-not (Test-Path $handlerCs)) {
        Write-Log "Bridge source not found at $handlerCs." "WARN"
        return $false
    }

    $outDll = Join-Path $TargetDir "OpenRCT.ConsoleExtension.dll"
    $wpfRefDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
    if (-not (Test-Path $wpfRefDir)) {
        $wpfRefDir = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\WPF"
    }

    $baseRefs = @(
        "/target:library",
        "/nologo",
        "/optimize+",
        "/out:`"$outDll`"",
        "/r:`"$(Join-Path $TargetDir 'OpenRCT.Core.dll')`"",
        "/r:`"$(Join-Path $TargetDir 'OpenRCT.UI.dll')`"",
        "/r:`"System.Xml.dll`"",
        "/r:`"System.Xaml.dll`"",
        "/r:`"$(Join-Path $wpfRefDir 'PresentationCore.dll')`"",
        "/r:`"$(Join-Path $wpfRefDir 'PresentationFramework.dll')`"",
        "/r:`"$(Join-Path $wpfRefDir 'WindowsBase.dll')`"",
        "/r:`"$cmExe`"",
        "/r:`"$mgmtProvider`""
    )

    foreach ($extraName in @(
        "AdminUI.WqlQueryEngine.dll",
        "AdminUI.ConsoleView.dll",
        "AdminUI.Common.dll",
        "AdminUI.Controls.dll",
        "AdminUI.Overview.dll",
        "AdminUI.UIResources.dll",
        "Microsoft.ConfigurationManagement.AdminConsole.Common.dll",
        "Microsoft.ConfigurationManagement.AdminConsole.Views.Common.dll"
    )) {
        $extraPath = Join-Path $AdminConsoleBin $extraName
        if (Test-Path $extraPath) {
            $baseRefs += "/r:`"$extraPath`""
        }
    }

    # Try compiling both Right-Click Action Handler + Embedded Dashboard Views
    if (Test-Path $viewsCs) {
        $viewsSourceToCompile = $viewsCs
        $extraUsings = @()
        $discoveredRefs = @()
        foreach ($typeName in @("OverviewControllerBase", "Overview", "IConsoleView2")) {
            $found = Find-TypeNamespaceInBin -AdminConsoleBin $AdminConsoleBin -TypeName $typeName
            if ($found) {
                Write-Log "Discovered $typeName in namespace '$($found.Namespace)' ($($found.Assembly))"
                $refArg = "/r:`"$($found.Assembly)`""
                if ($baseRefs -notcontains $refArg -and $discoveredRefs -notcontains $refArg) {
                    $discoveredRefs += $refArg
                }
                if ($found.Namespace -and $found.Namespace -ne "Microsoft.ConfigurationManagement.AdminConsole") {
                    $stmt = "using $($found.Namespace);"
                    if ($extraUsings -notcontains $stmt) { $extraUsings += $stmt }
                }
            }
        }
        if ($extraUsings.Count -gt 0) {
            $tempViews = Join-Path $env:TEMP "OpenRCT_DashboardConsoleViews_Bound.cs"
            $rawViews = Get-Content -Path $viewsCs -Raw
            Set-Content -Path $tempViews -Value (($extraUsings -join "`r`n") + "`r`n" + $rawViews) -Encoding UTF8
            $viewsSourceToCompile = $tempViews
        }

        $fullArgs = $baseRefs + $discoveredRefs + @("`"$handlerCs`"", "`"$viewsSourceToCompile`"")
        $code = Invoke-CscCompile -CscPath $csc -ArgsList $fullArgs
        if ($code -eq 0) {
            Write-Log "Successfully bound OpenRCT.ConsoleExtension.dll (Actions + Embedded Dashboards) to local MECM Console."
            return $true
        }
        Write-Log "Full bridge compile exited with code $code; retrying with OpenRctActionHandler.cs only so Right-Click menus work." "WARN"
    }

    $handlerOnlyArgs = $baseRefs + @("`"$handlerCs`"")
    $code2 = Invoke-CscCompile -CscPath $csc -ArgsList $handlerOnlyArgs
    if ($code2 -eq 0) {
        Write-Log "Successfully bound OpenRCT.ConsoleExtension.dll (Right-Click Actions) to local MECM Console."
        return $true
    }

    Write-Log "Local bridge binding failed (exit code $code2). See $script:LogFile for details." "ERROR"
    return $false
}

try {
    Write-Log "=== OpenRCT Installer started (User: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)) ==="
    Write-Log "Parameters: InstallDir='$InstallDir' ConsoleRoot='$ConsoleRoot' Mode='$Mode' SkipBinaryCopy=$SkipBinaryCopy"

    if (-not (Test-IsAdministrator)) {
        throw "Install-OpenRCT.ps1 must be run from an elevated (Administrator) PowerShell session."
    }

    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $packageRoot = Split-Path -Parent $scriptDir

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

    if (-not $SkipBinaryCopy) {
        $binSource = Join-Path $packageRoot "bin"
        if (Test-Path $binSource) {
            Write-Log "Copying binaries from '$binSource' to '$InstallDir'..."
            Copy-Item -Path (Join-Path $binSource "*") -Destination $InstallDir -Recurse -Force
        }
        foreach ($sub in @("scripts", "console-bridge-src", "console-extensions")) {
            $srcSub = Join-Path $packageRoot $sub
            $dstSub = Join-Path $InstallDir $sub
            if ((Test-Path $srcSub) -and ($srcSub.TrimEnd('\') -ne $dstSub.TrimEnd('\'))) {
                Copy-Item -Path $srcSub -Destination $InstallDir -Recurse -Force
            }
        }
    }

    Get-ChildItem -Path $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

    $detectedConsole = Find-MecmConsoleRoot -ExplicitPath $ConsoleRoot
    if (-not $detectedConsole) {
        Write-Log "MECM Admin Console was NOT detected. OpenRCT standalone app is installed at '$InstallDir', but console XML extensions were skipped. Pass -ConsoleRoot 'C:\...\AdminConsole' to register manually." "WARN"
    } else {
        Write-Log "Detected MECM Admin Console root: $detectedConsole"
        $consoleBin = Join-Path $detectedConsole "bin"
        $bridgeSource = Join-Path $InstallDir "console-bridge-src"
        if (-not (Test-Path $bridgeSource)) { $bridgeSource = Join-Path $packageRoot "console-bridge-src" }

        $effectiveMode = $Mode
        if ($Mode -eq "Assembly") {
            $boundOk = Build-LocalConsoleExtension -AdminConsoleBin $consoleBin -TargetDir $InstallDir -BridgeSourceDir $bridgeSource
            if (-not $boundOk) {
                Write-Log "Falling back to Executable mode XML (with Named-Pipe multi-select aggregator) because Assembly binding did not complete." "WARN"
                $effectiveMode = "Executable"
            }
        }

        $xmlTemplatesRoot = Join-Path $InstallDir "console-extensions\XmlStorage\Extensions"
        if (-not (Test-Path $xmlTemplatesRoot)) {
            $xmlTemplatesRoot = Join-Path $packageRoot "console-extensions\XmlStorage\Extensions"
        }
        if (-not (Test-Path $xmlTemplatesRoot)) {
            $xmlTemplatesRoot = Join-Path (Split-Path -Parent $packageRoot) "console-extensions\XmlStorage\Extensions"
        }
        Write-Log "Using XML templates from: $xmlTemplatesRoot (EffectiveMode=$effectiveMode)"

        $deviceActionTemplate = if ($effectiveMode -eq "Assembly") {
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
            $destFile = Join-Path $destDir "OpenRCT_DeviceActions.xml"
            $xml = (Get-Content -Path $deviceActionTemplate -Raw).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
            Set-Content -Path $destFile -Value $xml -Encoding UTF8
            Write-Log "Deployed Device Actions XML: $destFile"
        }

        if ($effectiveMode -eq "Assembly" -and (Test-Path $collectionActionTemplate)) {
            $colDestDir = Join-Path $detectedConsole "XmlStorage\Extensions\Actions\a92615d6-9df3-49ba-a8c9-6ecb0e8b956b"
            New-Item -ItemType Directory -Path $colDestDir -Force | Out-Null
            $colDestFile = Join-Path $colDestDir "OpenRCT_CollectionActions.xml"
            $colXml = (Get-Content -Path $collectionActionTemplate -Raw).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
            Set-Content -Path $colDestFile -Value $colXml -Encoding UTF8
            Write-Log "Deployed Collection Actions XML: $colDestFile"
        }

        if ($effectiveMode -eq "Assembly" -and (Test-Path $nodesTemplate)) {
            $overviewGuid = Resolve-AssetsOverviewNodeGuid -AdminConsoleRoot $detectedConsole
            $nodeDestDir = Join-Path $detectedConsole "XmlStorage\Extensions\Nodes\$overviewGuid"
            New-Item -ItemType Directory -Path $nodeDestDir -Force | Out-Null
            $nodeDestFile = Join-Path $nodeDestDir "OpenRCT_Nodes.xml"
            $nodeXml = (Get-Content -Path $nodesTemplate -Raw).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
            Set-Content -Path $nodeDestFile -Value $nodeXml -Encoding UTF8
            Write-Log "Deployed Console Dashboard Nodes XML: $nodeDestFile"
        }

        Write-Log "NOTE for MECM 2103+: If extensions still do not appear after restarting the Console, verify Hierarchy Settings -> 'Only allow console extensions that are approved for the hierarchy' is UNCHECKED (Administration -> Site Configuration -> Sites -> Hierarchy Settings)."
    }

    # Ensure Start Menu shortcut exists for portable ZIP installs
    if (-not $SkipBinaryCopy) {
        $startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\OpenRCT"
        New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
        $wsh = New-Object -ComObject WScript.Shell
        $shortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "OpenRCT.lnk"))
        $shortcut.TargetPath = Join-Path $InstallDir "OpenRCT.App.exe"
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.Description = "OpenRCT - Open Source Right Click Tools for MECM"
        $shortcut.Save()
    }

    if (-not [string]::IsNullOrWhiteSpace($PsExecPath)) {
        $appDataDir = Join-Path $env:APPDATA "OpenRCT"
        New-Item -ItemType Directory -Path $appDataDir -Force | Out-Null
        $settingsFile = Join-Path $appDataDir "settings.json"
        if (-not (Test-Path $settingsFile)) {
            $escaped = $PsExecPath.Replace('\', '\\')
            Set-Content -Path $settingsFile -Value "{`n  `"PsExecPath`": `"$escaped`"`n}" -Encoding UTF8
            Write-Log "Pre-configured PsExecPath in $settingsFile"
        }
    }

    Write-Log "=== OpenRCT installation finished successfully ==="
} catch {
    Write-Log "INSTALL FAILED: $($_.Exception.ToString())" "ERROR"
    throw
}
