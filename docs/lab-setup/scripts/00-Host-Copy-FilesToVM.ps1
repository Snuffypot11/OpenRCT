<#
.SYNOPSIS
    Copies a local file or folder from the Hyper-V host into a running lab VM via Hyper-V Guest Services.
.DESCRIPTION
    Transfers lab scripts, installers (SQL, MECM, ADK, LAPS, PsExec, 7-Zip), or freshly built
    OpenCMTools artifacts from the host into an isolated lab VM without enabling any SMB/network
    sharing between the host OS and the lab network.
.PARAMETER VMName
    Target Hyper-V VM name (e.g., LAB-DC01, LAB-CM01, LAB-WS01, LAB-WS02).
.PARAMETER SourcePath
    Local file or directory path on the Hyper-V host.
.PARAMETER DestinationPath
    Destination file or directory path inside the VM (e.g., C:\LabScripts or C:\Install\OpenCMTools.msi).
.EXAMPLE
    .\00-Host-Copy-FilesToVM.ps1 -VMName LAB-DC01 -SourcePath ".\scripts" -DestinationPath "C:\LabScripts"
.EXAMPLE
    .\00-Host-Copy-FilesToVM.ps1 -VMName LAB-CM01 -SourcePath "C:\Dev\OpenCMTools\OpenCMTools.msi" -DestinationPath "C:\Install\OpenCMTools.msi"
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VMName,

    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationPath
)

$ErrorActionPreference = "Stop"

$vm = Get-VM -Name $VMName -ErrorAction Stop
if ($vm.State -ne "Running") {
    throw "VM '$VMName' is currently '$($vm.State)'. Start the VM and wait for Windows to boot before copying files."
}

$guestSvc = Get-VMIntegrationService -VMName $VMName -Name "Guest Service Interface" -ErrorAction SilentlyContinue
if (-not $guestSvc -or -not $guestSvc.Enabled) {
    Write-Host "Enabling Hyper-V Guest Service Interface on '$VMName'..." -ForegroundColor Cyan
    Enable-VMIntegrationService -VMName $VMName -Name "Guest Service Interface"
}

$resolvedSource = (Resolve-Path -Path $SourcePath).Path
$item = Get-Item -Path $resolvedSource

if (-not $item.PSIsContainer) {
    Write-Host "Copying file '$resolvedSource' -> ${VMName}:${DestinationPath}" -ForegroundColor Cyan
    Copy-VMFile -Name $VMName -SourcePath $resolvedSource -DestinationPath $DestinationPath -CreateFullPath -FileSource Host -Force
    Write-Host "Copy complete." -ForegroundColor Green
    return
}

$files = Get-ChildItem -Path $resolvedSource -File -Recurse
if ($files.Count -eq 0) {
    Write-Warning "No files found under '$resolvedSource'."
    return
}

$destRoot = $DestinationPath.TrimEnd('\')
$copied = 0
foreach ($file in $files) {
    $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart('\')
    $targetPath = Join-Path -Path $destRoot -ChildPath $relative
    Write-Host "  [$($copied + 1)/$($files.Count)] $relative -> ${VMName}:${targetPath}" -ForegroundColor Gray
    Copy-VMFile -Name $VMName -SourcePath $file.FullName -DestinationPath $targetPath -CreateFullPath -FileSource Host -Force
    $copied++
}

Write-Host "Successfully copied $copied file(s) to ${VMName}:${destRoot}" -ForegroundColor Green
