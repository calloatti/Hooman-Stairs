param (
    [Parameter(Mandatory=$true)]
    [string]$ProjectFullPath,

    [Parameter(Mandatory=$true)]
    [string]$TargetDir
)

$ErrorActionPreference = "Stop"

# 1. Source Variables (Parsed from MSBuild Input)
$ProjectFullPath = (Get-Item $ProjectFullPath).FullName
$CsprojName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectFullPath)

$ProjectDir = Split-Path $ProjectFullPath -Parent
$ProjectFolderName = Split-Path $ProjectDir -Leaf
$RepoRootFolder = Split-Path $ProjectDir -Parent

# 2. Destination Variables
$DefinedModsFolder = Join-Path $env:USERPROFILE "Documents\Timberborn\Mods"
$DestModFolderRoot = Join-Path $DefinedModsFolder $CsprojName
$DestModFolder = Join-Path $DestModFolderRoot $ProjectFolderName

Write-Host "PreBuild: Starting build preparation for $CsprojName ($ProjectFolderName)..."

# 3. Execution Step 1: Safety Backup of workshop_data.json
$WorkshopDataSrc = Join-Path $DestModFolderRoot "workshop_data.json"
$WorkshopDataDest = Join-Path $RepoRootFolder "workshop_data.json"

if (Test-Path $WorkshopDataSrc) {
    Write-Host "PreBuild: Backing up workshop_data.json from $DestModFolderRoot to $RepoRootFolder"
    Copy-Item -Path $WorkshopDataSrc -Destination $WorkshopDataDest -Force
}

# 4. Execution Step 2: Clean MSBuild Target Directory
if (Test-Path $TargetDir) {
    Write-Host "PreBuild: Cleaning local TargetDir ($TargetDir)"
    Get-ChildItem $TargetDir -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
}

# 5. Execution Step 3: Surgically Clean the Destination Subfolder (e.g., Version-1.1)
if (Test-Path $DestModFolder) {
    Write-Host "PreBuild: Cleaning destination subfolder ($DestModFolder)"
    # Note: We are wiping the subfolder only, ignoring DestModFolderRoot to preserve Workshop ID and other versions
    Get-ChildItem -Path $DestModFolder -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
} else {
    Write-Host "PreBuild: Destination subfolder does not exist yet. Creating it..."
    New-Item -ItemType Directory -Force -Path $DestModFolder | Out-Null
}

Write-Host "PreBuild: Complete. Ready for MSBuild."