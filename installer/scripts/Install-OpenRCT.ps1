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
        [xml]$doc = Get-Content -Path $assetXmlPath -Raw -Encoding UTF8
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

function Get-ConsoleScanAssemblies {
    param([string]$AdminConsoleBin)
    $candidates = @()
    $cmExe = Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.exe"
    if (Test-Path $cmExe) { $candidates += $cmExe }
    foreach ($pattern in @("AdminUI*.dll", "Microsoft.ConfigurationManagement*.dll", "Microsoft.EnterpriseManagement*.dll")) {
        $candidates += (Get-ChildItem -Path $AdminConsoleBin -Filter $pattern -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    }
    return ($candidates | Select-Object -Unique)
}

function Load-ConsoleAssemblySafe {
    param([string]$AsmPath)
    $asm = $null
    try { $asm = [System.Reflection.Assembly]::LoadFrom($AsmPath) } catch { }
    if ($null -eq $asm) {
        try { $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($AsmPath) } catch { }
    }
    return $asm
}

function Find-AllTypesInBin {
    param([string]$AdminConsoleBin, [string]$TypeName)
    $matches = @()
    foreach ($asmPath in (Get-ConsoleScanAssemblies -AdminConsoleBin $AdminConsoleBin)) {
        try {
            $asm = Load-ConsoleAssemblySafe -AsmPath $asmPath
            if ($null -eq $asm) { continue }
            $types = @()
            try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
            foreach ($t in $types) {
                if ($t.Name -eq $TypeName) {
                    $matches += @{ Namespace = $t.Namespace; Assembly = $asmPath; Type = $t }
                }
            }
        } catch {
            # Ignore unreadable assembly
        }
    }
    return $matches
}

function Find-ConsoleViewBaseClassInBin {
    param([string]$AdminConsoleBin)
    $candidates = @()
    foreach ($asmPath in (Get-ConsoleScanAssemblies -AdminConsoleBin $AdminConsoleBin)) {
        try {
            $asm = Load-ConsoleAssemblySafe -AsmPath $asmPath
            if ($null -eq $asm) { continue }
            $types = @()
            try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
            foreach ($t in $types) {
                if (-not $t.IsClass -or -not $t.IsPublic -or $t.IsSealed) { continue }
                $ifaces = @()
                try { $ifaces = @($t.GetInterfaces() | ForEach-Object { $_.Name }) } catch { }
                if ($ifaces -contains "IConsoleView2") {
                    $flags = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,FlattenHierarchy"
                    $pCtrl = $t.GetProperty("TypeOfViewController", $flags)
                    $pView = $t.GetProperty("TypeOfView", $flags)
                    if ($pCtrl -and $pView) {
                        $candidates += @{ Namespace = $t.Namespace; Assembly = $asmPath; Type = $t }
                    }
                }
            }
        } catch {
        }
    }
    return $candidates
}

function Write-TypeDiagnostic {
    param([string]$TypeName, $FoundInfo)
    if (-not $FoundInfo -or -not $FoundInfo.Type) {
        Write-Log "SDK diagnostic: type '$TypeName' was NOT found in AdminConsole\bin." "WARN"
        return
    }
    try {
        $t = $FoundInfo.Type
        $kind = if ($t.IsInterface) { "interface" } elseif ($t.IsAbstract) { "abstract class" } else { "class" }
        $baseName = if ($t.BaseType) { $t.BaseType.FullName } else { "(none)" }
        $ifaceList = @($t.GetInterfaces() | ForEach-Object { $_.FullName }) -join ", "
        $flags = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,DeclaredOnly"
        $props = @($t.GetProperties($flags) | ForEach-Object {
            $pt = if ($_.PropertyType) { $_.PropertyType.Name } else { "?" }
            $gm = $_.GetGetMethod($true)
            $acc = if ($gm -and $gm.IsPublic) { "public" } else { "protected/nonpublic" }
            "$acc $pt $($_.Name)"
        }) -join ", "
        $methods = @($t.GetMethods($flags) | Where-Object { -not $_.IsSpecialName } | ForEach-Object {
            $rt = if ($_.ReturnType) { $_.ReturnType.Name } else { "?" }
            $ps = @($_.GetParameters() | ForEach-Object {
                $ptn = if ($_.ParameterType) { $_.ParameterType.Name } else { "?" }
                "$ptn $($_.Name)"
            }) -join ", "
            "$rt $($_.Name)($ps)"
        }) -join "; "
        Write-Log "SDK diagnostic [$TypeName]: ($kind) FullName=$($t.FullName), Base=$baseName, Ifaces=[$ifaceList], Asm=$($FoundInfo.Assembly)"
        Write-Log "SDK diagnostic [$TypeName] Props: $props"
        Write-Log "SDK diagnostic [$TypeName] Methods: $methods"
    } catch {
        Write-Log "SDK diagnostic [$TypeName] failed: $($_.Exception.Message)" "WARN"
    }
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
    $result = @{ ActionsBound = $false; DashboardsBound = $false }

    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) {
        $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
    }
    if (-not (Test-Path $csc)) {
        Write-Log "csc.exe not found; using prebuilt OpenRCT.ConsoleExtension.dll." "WARN"
        return $result
    }

    $cmExe = Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.exe"
    $mgmtProvider = Join-Path $AdminConsoleBin "Microsoft.ConfigurationManagement.ManagementProvider.dll"
    if (-not (Test-Path $cmExe) -or -not (Test-Path $mgmtProvider)) {
        Write-Log "Required MECM Console assemblies missing in $AdminConsoleBin (checked Microsoft.ConfigurationManagement.exe and Microsoft.ConfigurationManagement.ManagementProvider.dll)." "WARN"
        return $result
    }

    $handlerCs = Join-Path $BridgeSourceDir "OpenRctActionHandler.cs"
    $viewsCs = Join-Path $BridgeSourceDir "DashboardConsoleViews.cs"
    if (-not (Test-Path $handlerCs)) {
        Write-Log "Bridge source not found at $handlerCs." "WARN"
        return $result
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
        "/r:`"System.Windows.Forms.dll`"",
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
        "AdminUI.ViewCommon.dll",
        "AdminUI.HomePageView.dll",
        "AdminUI.UIResources.dll",
        "Microsoft.ConfigurationManagement.AdminConsole.Common.dll",
        "Microsoft.ConfigurationManagement.AdminConsole.Views.Common.dll"
    )) {
        $extraPath = Join-Path $AdminConsoleBin $extraName
        if (Test-Path $extraPath) {
            $baseRefs += "/r:`"$extraPath`""
        }
    }

    # Include all Microsoft.EnterpriseManagement*.dll (WpfViews, ConsoleFramework, ViewFramework, Foundation, etc.)
    $enterpriseDlls = Get-ChildItem -Path $AdminConsoleBin -Filter "Microsoft.EnterpriseManagement*.dll" -ErrorAction SilentlyContinue
    $dashboardRefs = @()
    foreach ($eDll in $enterpriseDlls) {
        $refArg = "/r:`"$($eDll.FullName)`""
        if ($baseRefs -notcontains $refArg -and $dashboardRefs -notcontains $refArg) {
            $dashboardRefs += $refArg
        }
    }

    # Try compiling both Right-Click Action Handler + Embedded Dashboard Views
    if (Test-Path $viewsCs) {
        $overviewCtrlMatches = @(Find-AllTypesInBin -AdminConsoleBin $AdminConsoleBin -TypeName "OverviewControllerBase")
        $overviewViewMatches = @(Find-AllTypesInBin -AdminConsoleBin $AdminConsoleBin -TypeName "Overview")
        $icv2Matches = @(Find-AllTypesInBin -AdminConsoleBin $AdminConsoleBin -TypeName "IConsoleView2")
        $viewBaseCandidates = @(Find-ConsoleViewBaseClassInBin -AdminConsoleBin $AdminConsoleBin)

        $overviewCtrl = $overviewCtrlMatches | Select-Object -First 1
        $overviewView = $overviewViewMatches | Where-Object { $_.Namespace -match "WpfViews" } | Select-Object -First 1
        if (-not $overviewView) { $overviewView = $overviewViewMatches | Select-Object -First 1 }

        # Prefer a class named IConsoleView2; otherwise a public base class implementing IConsoleView2; otherwise the IConsoleView2 interface
        $icv2Class = $icv2Matches | Where-Object { $_.Type.IsClass } | Select-Object -First 1
        $viewBaseClass = if ($icv2Class) { $icv2Class } elseif ($viewBaseCandidates.Count -gt 0) {
            ($viewBaseCandidates | Where-Object { $_.Type.IsAbstract } | Select-Object -First 1)
        } else { $null }
        if (-not $viewBaseClass -and $viewBaseCandidates.Count -gt 0) {
            $viewBaseClass = $viewBaseCandidates | Select-Object -First 1
        }
        $icv2Interface = $icv2Matches | Where-Object { $_.Type.IsInterface } | Select-Object -First 1

        foreach ($m in @($overviewCtrl, $overviewView, $viewBaseClass, $icv2Interface)) {
            if ($m -and $m.Assembly) {
                $refArg = "/r:`"$($m.Assembly)`""
                if ($baseRefs -notcontains $refArg -and $dashboardRefs -notcontains $refArg) {
                    $dashboardRefs += $refArg
                }
            }
        }

        if ($overviewCtrl) { Write-Log "Discovered OverviewControllerBase: $($overviewCtrl.Type.FullName) ($($overviewCtrl.Assembly))" }
        if ($overviewView) { Write-Log "Discovered Overview: $($overviewView.Type.FullName) ($($overviewView.Assembly))" }
        if ($viewBaseClass) { Write-Log "Discovered ViewDescription base class: $($viewBaseClass.Type.FullName) ($($viewBaseClass.Assembly))" }
        elseif ($icv2Interface) { Write-Log "Discovered IConsoleView2 interface: $($icv2Interface.Type.FullName) ($($icv2Interface.Assembly))" }

        $rawViews = Get-Content -Path $viewsCs -Raw -Encoding UTF8
        if ($overviewCtrl) {
            $rawViews = $rawViews.Replace(": OverviewControllerBase", ": $($overviewCtrl.Type.FullName)")
        }
        if ($overviewView) {
            $rawViews = $rawViews.Replace("typeof(Overview)", "typeof($($overviewView.Type.FullName))")
        }

        if ($viewBaseClass) {
            $baseType = $viewBaseClass.Type
            $rawViews = $rawViews.Replace(": IConsoleView2", ": $($baseType.FullName)")
            $flags = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,FlattenHierarchy"
            $pView = $baseType.GetProperty("TypeOfView", $flags)
            $pCtrl = $baseType.GetProperty("TypeOfViewController", $flags)
            if ($pView -and $pView.GetGetMethod($true) -and $pView.GetGetMethod($true).IsPublic) {
                $rawViews = $rawViews.Replace("protected override Type TypeOfView", "public override Type TypeOfView")
            }
            if ($pCtrl -and $pCtrl.GetGetMethod($true) -and $pCtrl.GetGetMethod($true).IsPublic) {
                $rawViews = $rawViews.Replace("protected override Type TypeOfViewController", "public override Type TypeOfViewController")
            }
            $mTryCfg = $baseType.GetMethod("TryConfigure", $flags)
            if ($mTryCfg -and (-not $mTryCfg.IsVirtual -or $mTryCfg.IsFinal)) {
                $rawViews = $rawViews.Replace("public override bool TryConfigure(", "public new bool TryConfigure(")
            }
        } elseif ($icv2Interface) {
            # Direct interface implementation fallback when no abstract ConsoleView base class was found
            $rawViews = $rawViews.Replace(": IConsoleView2", ": $($icv2Interface.Type.FullName)")
            $rawViews = $rawViews.Replace("protected override Type TypeOfView", "public Type TypeOfView")
            $rawViews = $rawViews.Replace("protected override Type TypeOfViewController", "public override Type TypeOfViewController")
            $rawViews = $rawViews.Replace("public override bool TryConfigure(", "public bool TryConfigure(")
            $rawViews = $rawViews.Replace("public new bool TryInitialize(", "public bool TryInitialize(")
            # Declare abstract TypeOfViewController on the base class so sealed subclasses override it cleanly
            $rawViews = $rawViews.Replace(
                "public Type TypeOfView",
                "public abstract Type TypeOfViewController { get; }`r`n`r`n        public Type TypeOfView"
            )
        }

        $tempViews = Join-Path $env:TEMP "OpenRCT_DashboardConsoleViews_Bound.cs"
        Set-Content -Path $tempViews -Value $rawViews -Encoding UTF8

        $fullArgs = $baseRefs + $dashboardRefs + @("`"$handlerCs`"", "`"$tempViews`"")
        $code = Invoke-CscCompile -CscPath $csc -ArgsList $fullArgs
        if ($code -eq 0) {
            Write-Log "Successfully bound OpenRCT.ConsoleExtension.dll (Actions + Embedded Dashboards) to local MECM Console."
            $result.ActionsBound = $true
            $result.DashboardsBound = $true
            return $result
        }

        Write-Log "Full bridge compile exited with code $code; logging SDK diagnostics and retrying with OpenRctActionHandler.cs only." "WARN"
        Write-TypeDiagnostic -TypeName "OverviewControllerBase" -FoundInfo $overviewCtrl
        Write-TypeDiagnostic -TypeName "Overview" -FoundInfo $overviewView
        foreach ($m in $icv2Matches) { Write-TypeDiagnostic -TypeName "IConsoleView2" -FoundInfo $m }
        foreach ($m in $viewBaseCandidates) { Write-TypeDiagnostic -TypeName "ViewBaseCandidate" -FoundInfo $m }
    }

    $handlerOnlyArgs = $baseRefs + @("`"$handlerCs`"")
    $code2 = Invoke-CscCompile -CscPath $csc -ArgsList $handlerOnlyArgs
    if ($code2 -eq 0) {
        Write-Log "Successfully bound OpenRCT.ConsoleExtension.dll (Right-Click Actions only) to local MECM Console."
        $result.ActionsBound = $true
        return $result
    }

    Write-Log "Local bridge binding failed (exit code $code2). See $script:LogFile for details." "ERROR"
    return $result
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

    # Record InstallDir in HKLM so OpenRCT.ConsoleExtension.dll can always locate OpenRCT.UI.dll
    try {
        New-Item -Path "HKLM:\SOFTWARE\OpenRCT" -Force | Out-Null
        Set-ItemProperty -Path "HKLM:\SOFTWARE\OpenRCT" -Name "InstallDir" -Value $InstallDir
    } catch {
        Write-Log "Warning: could not write HKLM:\SOFTWARE\OpenRCT InstallDir: $($_.Exception.Message)" "WARN"
    }

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
        $dashboardsBound = $false
        if ($Mode -eq "Assembly") {
            $bindResult = Build-LocalConsoleExtension -AdminConsoleBin $consoleBin -TargetDir $InstallDir -BridgeSourceDir $bridgeSource
            $dashboardsBound = [bool]$bindResult.DashboardsBound
            if (-not $bindResult.ActionsBound) {
                Write-Log "Falling back to Executable mode XML (with Named-Pipe multi-select aggregator) because Assembly binding did not complete." "WARN"
                $effectiveMode = "Executable"
            } else {
                # Deploy compiled bridge DLL + installdir marker into AdminConsole\bin (required by MECM ViewAssemblyDescription)
                $builtBridgeDll = Join-Path $InstallDir "OpenRCT.ConsoleExtension.dll"
                if (Test-Path $builtBridgeDll) {
                    try {
                        Copy-Item -Path $builtBridgeDll -Destination (Join-Path $consoleBin "OpenRCT.ConsoleExtension.dll") -Force
                        Set-Content -Path (Join-Path $consoleBin "OpenRCT.installdir.txt") -Value $InstallDir -Encoding UTF8
                        Write-Log "Deployed OpenRCT.ConsoleExtension.dll and OpenRCT.installdir.txt to $consoleBin"
                    } catch {
                        Write-Log "Could not copy OpenRCT.ConsoleExtension.dll to $consoleBin (close MECM Console and re-run Repair): $($_.Exception.Message)" "WARN"
                    }
                }
            }
        }

        $xmlTemplatesRoot = Join-Path $InstallDir "console-extensions\XmlStorage\Extensions"
        if (-not (Test-Path $xmlTemplatesRoot)) {
            $xmlTemplatesRoot = Join-Path $packageRoot "console-extensions\XmlStorage\Extensions"
        }
        if (-not (Test-Path $xmlTemplatesRoot)) {
            $xmlTemplatesRoot = Join-Path (Split-Path -Parent $packageRoot) "console-extensions\XmlStorage\Extensions"
        }
        Write-Log "Using XML templates from: $xmlTemplatesRoot (EffectiveMode=$effectiveMode, DashboardsBound=$dashboardsBound)"

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
            $xml = (Get-Content -Path $deviceActionTemplate -Raw -Encoding UTF8).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
            Set-Content -Path $destFile -Value $xml -Encoding UTF8
            Write-Log "Deployed Device Actions XML: $destFile"
        }

        if ($effectiveMode -eq "Assembly" -and (Test-Path $collectionActionTemplate)) {
            $collectionGuids = @(
                "a92615d6-9df3-49ba-a8c9-6ecb0e8b956b", # Device Collections (standard)
                "e92615d6-9df3-49ba-a8c9-6ecb0e8b956b"  # Device Collections (MECM 2010+ alternate)
            )
            foreach ($colGuid in $collectionGuids) {
                $colDestDir = Join-Path $detectedConsole "XmlStorage\Extensions\Actions\$colGuid"
                New-Item -ItemType Directory -Path $colDestDir -Force | Out-Null
                $colDestFile = Join-Path $colDestDir "OpenRCT_CollectionActions.xml"
                $colXml = (Get-Content -Path $collectionActionTemplate -Raw -Encoding UTF8).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
                Set-Content -Path $colDestFile -Value $colXml -Encoding UTF8
                Write-Log "Deployed Collection Actions XML: $colDestFile"
            }
        }

        if (Test-Path $nodesTemplate) {
            $overviewGuid = Resolve-AssetsOverviewNodeGuid -AdminConsoleRoot $detectedConsole
            $nodeDestDir = Join-Path $detectedConsole "XmlStorage\Extensions\Nodes\$overviewGuid"
            $nodeDestFile = Join-Path $nodeDestDir "OpenRCT_Nodes.xml"
            if ($dashboardsBound) {
                New-Item -ItemType Directory -Path $nodeDestDir -Force | Out-Null
                $nodeXml = (Get-Content -Path $nodesTemplate -Raw -Encoding UTF8).Replace("{{OPENRCT_INSTALL_DIR}}", $InstallDir)
                Set-Content -Path $nodeDestFile -Value $nodeXml -Encoding UTF8
                Write-Log "Deployed Console Dashboard Nodes XML: $nodeDestFile (InlineViews=True)"
            } else {
                # Deploying RootNodeDescription without ViewAssemblyDescriptions crashes MECM Console
                # in SccmTreeNavigationNodeProviderBase.AddSavedSearchMenuGroup (ArgumentOutOfRangeException).
                if (Test-Path $nodeDestFile) {
                    Remove-Item -Path $nodeDestFile -Force -ErrorAction SilentlyContinue
                }
                Write-Log "Skipped deploying OpenRCT_Nodes.xml because DashboardConsoleViews did not bind (prevents MECM Console crash). Use standalone OpenRCT app for dashboards and share C:\ProgramData\OpenRCT\install.log." "WARN"
            }
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
