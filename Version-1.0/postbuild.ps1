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

Write-Host "PostBuild: Starting deployment for $CsprojName ($ProjectFolderName)..."

# 3. Execution Step 1: Directory Verification
if (-not (Test-Path $DestModFolder)) {
    Write-Host "PostBuild: Creating missing destination folder ($DestModFolder)"
    New-Item -ItemType Directory -Force -Path $DestModFolder | Out-Null
}

# 4. Execution Step 2: Deploy Version-Specific Binaries
Write-Host "PostBuild: Deploying compiled binaries to $DestModFolder"
Copy-Item -Path (Join-Path $TargetDir "*") -Destination $DestModFolder -Recurse -Force -Exclude "thumbnail.jpg","thumbnail.png","workshop_data.json"

# 5. Execution Step 3: Deploy Root Assets
$SpecialFiles = @("thumbnail.jpg", "thumbnail.png", "workshop_data.json", "README.md")
foreach ($file in $SpecialFiles) {
    $srcFile = Join-Path $RepoRootFolder $file
    
    if (Test-Path $srcFile) {
        Write-Host "PostBuild: Deploying root asset $file to $DestModFolderRoot"
        if (-not (Test-Path $DestModFolderRoot)) {
            New-Item -ItemType Directory -Force -Path $DestModFolderRoot | Out-Null
        }
        Copy-Item -Path $srcFile -Destination $DestModFolderRoot -Force
    }
}

# 6. Execution Step 4: Sanitation Pass
Write-Host "PostBuild: Cleaning up unwanted build scripts and markdown files in $DestModFolderRoot"
Get-ChildItem -Path $DestModFolderRoot -Include "*.ps1", "AGENTS.md", "*.zip" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "PostBuild: Deployment complete."