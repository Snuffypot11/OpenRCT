<#
.SYNOPSIS
    Cleanly uninstalls OpenCMTools binaries, Start Menu shortcuts, and MECM Console XML extensions.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\OpenCMTools",
    [string]$ConsoleRoot = "",
    [switch]$SkipBinaryRemove
)

$ErrorActionPreference = "Stop"

$InstallDir = [System.IO.Path]::GetFullPath($InstallDir.Trim().Trim('"')).TrimEnd('\')
if (-not [string]::IsNullOrWhiteSpace($ConsoleRoot)) {
    $ConsoleRoot = [System.IO.Path]::GetFullPath($ConsoleRoot.Trim().Trim('"')).TrimEnd('\')
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
    foreach ($cand in @(
        $env:SMS_ADMIN_UI_PATH,
        [Environment]::GetEnvironmentVariable("SMS_ADMIN_UI_PATH", "Machine"),
        "${env:ProgramFiles(x86)}\Microsoft Endpoint Manager\AdminConsole",
        "${env:ProgramFiles(x86)}\Microsoft Configuration Manager\AdminConsole",
        "$env:ProgramFiles\Microsoft Endpoint Manager\AdminConsole",
        "$env:ProgramFiles\Microsoft Configuration Manager\AdminConsole"
    )) {
        $resolved = Resolve-ConsoleRootFromCandidate -Path $cand
        if ($resolved) { return $resolved }
    }
    foreach ($rk in @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\ConfigMgr10\Setup",
        "HKLM:\SOFTWARE\Microsoft\ConfigMgr10\Setup",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\SMS\Setup",
        "HKLM:\SOFTWARE\Microsoft\SMS\Setup"
    )) {
        if (Test-Path $rk) {
            $uiDir = (Get-ItemProperty -Path $rk -Name "UI Installation Directory" -ErrorAction SilentlyContinue)."UI Installation Directory"
            $resolved = Resolve-ConsoleRootFromCandidate -Path $uiDir
            if ($resolved) { return $resolved }
        }
    }
    return $null
}

$detectedConsole = Find-MecmConsoleRoot -ExplicitPath $ConsoleRoot
if ($detectedConsole) {
    $extRoot = Join-Path $detectedConsole "XmlStorage\Extensions"
    if (Test-Path $extRoot) {
        foreach ($pattern in @("OpenCMTools_*.xml", "OpenRCT_*.xml")) {
            Get-ChildItem -Path $extRoot -Recurse -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Host "Removing console extension XML: $($_.FullName)" -ForegroundColor Yellow
                Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
            }
        }
    }
    $consoleBin = Join-Path $detectedConsole "bin"
    foreach ($binFile in @("OpenCMTools.ConsoleExtension.dll", "OpenCMTools.Icons.dll", "OpenCMTools.installdir.txt", "OpenRCT.ConsoleExtension.dll", "OpenRCT.Icons.dll", "OpenRCT.installdir.txt")) {
        $p = Join-Path $consoleBin $binFile
        if (Test-Path $p) {
            Write-Host "Removing console bin file: $p" -ForegroundColor Yellow
            Remove-Item -Path $p -Force -ErrorAction SilentlyContinue
        }
    }
    foreach ($oldPattern in @("OpenCMTools.ConsoleExtension.dll.old.*", "OpenCMTools.Icons.dll.old.*", "OpenRCT.ConsoleExtension.dll.old.*", "OpenRCT.Icons.dll.old.*")) {
        Get-ChildItem -Path $consoleBin -Filter $oldPattern -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

foreach ($regKey in @("HKLM:\SOFTWARE\OpenCMTools", "HKLM:\SOFTWARE\OpenRCT")) {
    if (Test-Path $regKey) {
        Remove-Item -Path $regKey -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $SkipBinaryRemove) {
    $startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\OpenCMTools"
    if (Test-Path $startMenuDir) {
        Remove-Item -Path $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path $InstallDir) {
        Write-Host "Removing $InstallDir..." -ForegroundColor Yellow
        Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "OpenCMTools uninstalled cleanly. Restart any open Configuration Manager Console." -ForegroundColor Green
