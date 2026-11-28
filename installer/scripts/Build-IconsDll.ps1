<#
.SYNOPSIS
    Builds OpenCMTools.Icons.dll with a native .NET Framework 4.8 ConsoleIcons.resources stream
    for MECM Admin Console <ImagesDescription> tree and context-menu icons.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IconsDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputDll,

    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($Version)) {
    $repoRoot = Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    if (Test-Path $propsPath) {
        try {
            [xml]$propsXml = Get-Content -Path $propsPath -Raw
            $asmVer = $propsXml.Project.PropertyGroup.AssemblyVersion | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
            if (-not [string]::IsNullOrWhiteSpace($asmVer)) {
                $Version = $asmVer.Trim()
            }
            else {
                $pkgVer = $propsXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
                if (-not [string]::IsNullOrWhiteSpace($pkgVer)) {
                    $trimmed = $pkgVer.Trim()
                    $Version = if (($trimmed -split '\.').Count -eq 3) { "$trimmed.0" } else { $trimmed }
                }
            }
        }
        catch {
            Write-Warning "Failed to read version from '$propsPath': $_"
        }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0.0"
    }
}

$resolvedIconsDir = [System.IO.Path]::GetFullPath($IconsDir)
$resolvedOutputDll = [System.IO.Path]::GetFullPath($OutputDll)
$outDir = Split-Path -Path $resolvedOutputDll -Parent
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$icoFiles = Get-ChildItem -Path $resolvedIconsDir -Filter "*.ico" | Sort-Object Name
if ($icoFiles.Count -eq 0) {
    throw "No .ico files found in '$resolvedIconsDir'."
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenCMTools_Icons_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    $resPath = Join-Path $tempDir "OpenCMTools.Icons.ConsoleIcons.resources"
    $rw = New-Object System.Resources.ResourceWriter($resPath)
    $loadedIcons = New-Object System.Collections.Generic.List[System.Drawing.Icon]
    try {
        foreach ($f in $icoFiles) {
            $icon = New-Object System.Drawing.Icon($f.FullName)
            $loadedIcons.Add($icon)
            $rw.AddResource($f.BaseName, $icon)
        }
        $rw.Generate()
    }
    finally {
        $rw.Close()
        foreach ($ic in $loadedIcons) {
            try { $ic.Dispose() } catch { }
        }
    }

    $asmInfoPath = Join-Path $tempDir "AssemblyInfo.cs"
    $asmInfo = @"
using System.Reflection;
[assembly: AssemblyTitle("OpenCMTools Console Icons")]
[assembly: AssemblyProduct("OpenCMTools")]
[assembly: AssemblyCompany("OpenCMTools Contributors")]
[assembly: AssemblyVersion("$Version")]
[assembly: AssemblyFileVersion("$Version")]
"@
    Set-Content -Path $asmInfoPath -Value $asmInfo -Encoding ASCII

    $cscCandidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    $csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $csc) {
        throw "Unable to locate .NET Framework 4.8 csc.exe."
    }

    $args = @(
        "/nologo",
        "/target:library",
        "/optimize+",
        "/out:$resolvedOutputDll",
        "/resource:$resPath,OpenCMTools.Icons.ConsoleIcons.resources",
        $asmInfoPath
    )
    & $csc @args
    if ($LASTEXITCODE -ne 0) {
        throw "csc.exe failed while building OpenCMTools.Icons.dll (exit code $LASTEXITCODE)."
    }

    Write-Host "Built $resolvedOutputDll with $($icoFiles.Count) native .NET 4.8 icon resource(s)."
}
finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
