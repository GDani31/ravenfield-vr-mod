<#
.SYNOPSIS
    Installs the Ravenfield VR Mod (BepInEx 5.4.23.5 + mod files).

.DESCRIPTION
    - Detects the Ravenfield install folder (or accepts -RavenfieldPath).
    - Installs BepInEx 5.4.23.5 if not already present.
    - Copies the VR mod files (DLLs, JSON configs, manifest) to the right
      subfolders inside the game directory.

    Designed to be run from a folder that contains the unzipped release
    files. If those files are missing it will fall back to the build
    output folder (bin/Release/net472), and finally offer to download the
    latest GitHub release.

.PARAMETER RavenfieldPath
    Path to the Ravenfield install folder. Auto-detected if omitted.

.PARAMETER SourceDir
    Folder containing the mod release files. Defaults to the script's
    own folder.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -RavenfieldPath "D:\Games\Ravenfield"
#>

[CmdletBinding()]
param(
    [string]$RavenfieldPath,
    [string]$SourceDir
)

$ErrorActionPreference = 'Stop'

$BepInExVersion = '5.4.23.5'
$BepInExUrl     = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$ModRepo        = 'GDani31/ravenfield-vr-mod'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Files that must exist in the source folder, and where they go (relative to game dir).
$FileMap = @(
    @{ Name = 'RavenfieldVRMod.dll';        Dest = 'BepInEx\plugins' },
    @{ Name = 'actions.json';               Dest = 'BepInEx\plugins' },
    @{ Name = 'bindings_oculus_touch.json'; Dest = 'BepInEx\plugins' },
    @{ Name = 'bindings_knuckles.json';     Dest = 'BepInEx\plugins' },
    @{ Name = 'bindings_vive_controller.json'; Dest = 'BepInEx\plugins' },
    @{ Name = 'Unity.XR.Management.dll';    Dest = 'ravenfield_Data\Managed' },
    @{ Name = 'Unity.XR.OpenVR.dll';        Dest = 'ravenfield_Data\Managed' },
    @{ Name = 'XRSDKOpenVR.dll';            Dest = 'ravenfield_Data\Plugins\x86_64' },
    @{ Name = 'openvr_api.dll';             Dest = 'ravenfield_Data\Plugins\x86_64' },
    @{ Name = 'UnitySubsystemsManifest.json'; Dest = 'ravenfield_Data\UnitySubsystems\XRSDKOpenVR' }
)

function Write-Step([string]$msg)    { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)      { Write-Host "    $msg" -ForegroundColor Green }
function Write-Info([string]$msg)    { Write-Host "    $msg" -ForegroundColor DarkGray }
function Write-Warnish([string]$msg) { Write-Host "    $msg" -ForegroundColor Yellow }

function Resolve-RavenfieldPath {
    if ($RavenfieldPath) {
        if (-not (Test-Path (Join-Path $RavenfieldPath 'ravenfield.exe'))) {
            throw "ravenfield.exe not found in '$RavenfieldPath'"
        }
        return (Resolve-Path $RavenfieldPath).Path
    }
    # Script lives inside or next to the game folder?
    $candidates = @(
        $ScriptDir,
        (Split-Path -Parent $ScriptDir),
        'C:\Program Files (x86)\Steam\steamapps\common\Ravenfield',
        'C:\Program Files\Steam\steamapps\common\Ravenfield',
        'D:\Steam\steamapps\common\Ravenfield',
        'E:\Steam\steamapps\common\Ravenfield'
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'ravenfield.exe'))) {
            return (Resolve-Path $c).Path
        }
    }
    while ($true) {
        $p = Read-Host 'Enter the path to your Ravenfield install folder'
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        if (Test-Path (Join-Path $p 'ravenfield.exe')) {
            return (Resolve-Path $p).Path
        }
        Write-Warnish "ravenfield.exe not found there. Try again."
    }
}

function Test-SourceComplete([string]$dir) {
    if (-not $dir -or -not (Test-Path $dir)) { return $false }
    foreach ($f in $FileMap) {
        if (-not (Test-Path (Join-Path $dir $f.Name))) { return $false }
    }
    return $true
}

function Get-LatestReleaseZipUrl {
    $api = "https://api.github.com/repos/$ModRepo/releases/latest"
    Write-Info "Querying $api"
    $headers = @{ 'User-Agent' = 'ravenfield-vr-mod-installer' }
    $rel = Invoke-RestMethod -Uri $api -Headers $headers
    $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $asset) { throw "No .zip asset found in latest release of $ModRepo" }
    return @{ Url = $asset.browser_download_url; Name = $asset.name; Tag = $rel.tag_name }
}

function Resolve-SourceDir {
    if ($SourceDir) {
        if (-not (Test-SourceComplete $SourceDir)) {
            throw "SourceDir '$SourceDir' is missing one or more required files."
        }
        return (Resolve-Path $SourceDir).Path
    }

    $candidates = @(
        $ScriptDir,
        (Join-Path $ScriptDir 'bin\Release\net472'),
        (Join-Path $ScriptDir '..\bin\Release\net472')
    )
    foreach ($c in $candidates) {
        if (Test-SourceComplete $c) { return (Resolve-Path $c).Path }
    }

    Write-Warnish "Could not find the mod files locally."
    $answer = Read-Host "Download the latest release from github.com/$ModRepo? [Y/n]"
    if ($answer -and $answer.Trim().ToLower() -eq 'n') {
        throw "Aborted. Place install.ps1 next to the unzipped release files and re-run."
    }

    $info = Get-LatestReleaseZipUrl
    Write-Info "Latest release: $($info.Tag) ($($info.Name))"
    $tmpZip = Join-Path $env:TEMP $info.Name
    $tmpDir = Join-Path $env:TEMP "ravenfield_vr_mod_$($info.Tag)"
    if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
    Write-Info "Downloading $($info.Url)"
    Invoke-WebRequest -Uri $info.Url -OutFile $tmpZip -UseBasicParsing
    Write-Info "Extracting to $tmpDir"
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force
    Remove-Item $tmpZip -Force

    # The release zip may extract files directly or into a single subfolder.
    if (Test-SourceComplete $tmpDir) { return $tmpDir }
    $sub = Get-ChildItem -Path $tmpDir -Directory | Select-Object -First 1
    if ($sub -and (Test-SourceComplete $sub.FullName)) { return $sub.FullName }
    throw "Downloaded release does not contain the expected files."
}

function Get-BepInExInstalledVersion([string]$gameDir) {
    $core = Join-Path $gameDir 'BepInEx\core\BepInEx.dll'
    if (-not (Test-Path $core)) { return $null }
    try {
        $vi = (Get-Item $core).VersionInfo
        if ($vi.ProductVersion) { return $vi.ProductVersion.Trim() }
        if ($vi.FileVersion)    { return $vi.FileVersion.Trim() }
    } catch { }
    return $null
}

function Install-BepInEx([string]$gameDir) {
    Write-Step "Installing BepInEx $BepInExVersion"
    $tmpZip = Join-Path $env:TEMP "BepInEx_win_x64_$BepInExVersion.zip"
    $tmpDir = Join-Path $env:TEMP "BepInEx_win_x64_$BepInExVersion"
    if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
    Write-Info "Downloading $BepInExUrl"
    Invoke-WebRequest -Uri $BepInExUrl -OutFile $tmpZip -UseBasicParsing
    Write-Info "Extracting"
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force
    Write-Info "Copying into $gameDir"
    Copy-Item -Path (Join-Path $tmpDir '*') -Destination $gameDir -Recurse -Force
    Remove-Item $tmpZip -Force
    Remove-Item $tmpDir -Recurse -Force
    Write-Ok "BepInEx $BepInExVersion installed."
}

function Install-ModFiles([string]$gameDir, [string]$source) {
    Write-Step "Installing mod files"
    Write-Info "From: $source"
    foreach ($f in $FileMap) {
        $src      = Join-Path $source $f.Name
        $destDir  = Join-Path $gameDir $f.Dest
        if (-not (Test-Path $src)) {
            throw "Missing required file: $src"
        }
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -Path $src -Destination (Join-Path $destDir $f.Name) -Force
        Write-Info "$($f.Dest)\$($f.Name)"
    }
    Write-Ok "Mod files copied."
}

# ---------- main ----------

Write-Host ""
Write-Host "Ravenfield VR Mod Installer" -ForegroundColor Cyan
Write-Host "===========================" -ForegroundColor Cyan
Write-Host ""

$gameDir = Resolve-RavenfieldPath
Write-Step "Ravenfield folder"
Write-Info $gameDir

$source = Resolve-SourceDir
Write-Step "Source files"
Write-Info $source

$existing = Get-BepInExInstalledVersion $gameDir
if ($existing -and $existing -like "$BepInExVersion*") {
    Write-Step "BepInEx"
    Write-Ok "Version $existing already installed. Skipping."
} elseif ($existing) {
    Write-Step "BepInEx"
    Write-Warnish "Found version $existing, replacing with $BepInExVersion."
    Install-BepInEx $gameDir
} else {
    Install-BepInEx $gameDir
}

Install-ModFiles $gameDir $source

Write-Host ""
Write-Host "All done. Launch Ravenfield with SteamVR running." -ForegroundColor Green
Write-Host ""
