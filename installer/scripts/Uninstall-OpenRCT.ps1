<#
.SYNOPSIS
    Cleanly uninstalls OpenRCT binaries, Start Menu shortcuts, and MECM Console XML extensions.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\OpenRCT",
    [string]$ConsoleRoot = ""
)

$ErrorActionPreference = "Stop"

function Find-MecmConsoleRoot {
    param([string]$ExplicitPath)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path $ExplicitPath)) { return $ExplicitPath }
    if (-not [string]::IsNullOrWhiteSpace($env:SMS_ADMIN_UI_PATH)) {
        $candidate = Split-Path -Path $env:SMS_ADMIN_UI_PATH -Parent
        if (Test-Path (Join-Path $candidate "XmlStorage")) { return $candidate }
    }
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Microsoft Endpoint Manager\AdminConsole",
        "${env:ProgramFiles(x86)}\Microsoft Configuration Manager\AdminConsole",
        "$env:ProgramFiles\Microsoft Endpoint Manager\AdminConsole"
    )) {
        if (Test-Path (Join-Path $p "XmlStorage")) { return $p }
    }
    return $null
}

$detectedConsole = Find-MecmConsoleRoot -ExplicitPath $ConsoleRoot
if ($detectedConsole) {
    $extRoot = Join-Path $detectedConsole "XmlStorage\Extensions"
    if (Test-Path $extRoot) {
        Get-ChildItem -Path $extRoot -Recurse -Filter "OpenRCT_*.xml" -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Removing console extension XML: $($_.FullName)" -ForegroundColor Yellow
            Remove-Item -Path $_.FullName -Force
        }
    }
}

$startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\OpenRCT"
if (Test-Path $startMenuDir) {
    Remove-Item -Path $startMenuDir -Recurse -Force
}

if (Test-Path $InstallDir) {
    Write-Host "Removing $InstallDir..." -ForegroundColor Yellow
    Remove-Item -Path $InstallDir -Recurse -Force
}

Write-Host "OpenRCT uninstalled cleanly. Restart any open Configuration Manager Console." -ForegroundColor Green
