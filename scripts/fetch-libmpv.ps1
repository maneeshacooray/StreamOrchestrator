#requires -Version 5.1
<#
.SYNOPSIS
    Downloads the native libmpv engine (libmpv-2.dll, x64) into libs/.

.DESCRIPTION
    The DLL is a large (~120 MB) native binary and is intentionally NOT committed to git
    (it exceeds GitHub's per-file limit and bloats the repo). Run this once after cloning so
    the app can play RTSP. The installer bundles the DLL at publish time, so end users who
    receive Setup.exe do not need to run this.

    Uses the baseline x86_64 build (not the -v3- AVX2 build) for the widest CPU compatibility.

.PARAMETER Latest
    Fetch the latest release instead of the pinned known-good version.
#>
param(
    [switch]$Latest
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$libsDir  = Join-Path $repoRoot 'libs'
$dllPath  = Join-Path $libsDir 'libmpv-2.dll'

# Pinned known-good release (update when bumping the engine).
$pinnedTag   = '2026-10-09-fd9265a905'
$pinnedAsset = 'mpv-dev-x86_64-20261009-git-fd9265a905.7z'

New-Item -ItemType Directory -Force -Path $libsDir | Out-Null

if (Test-Path $dllPath) {
    Write-Host "libmpv-2.dll already present at $dllPath" -ForegroundColor Green
    exit 0
}

if ($Latest) {
    Write-Host 'Resolving latest mpv-winbuild release...'
    $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest' -Headers @{ 'User-Agent' = 'StreamOrchestrator' }
    $asset = $rel.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d' -and $_.name -notmatch '-v3-' } | Select-Object -First 1
    if (-not $asset) { throw 'Could not find a baseline mpv-dev-x86_64 asset in the latest release.' }
    $url  = $asset.browser_download_url
    $name = $asset.name
} else {
    $name = $pinnedAsset
    $url  = "https://github.com/zhongfly/mpv-winbuild/releases/download/$pinnedTag/$pinnedAsset"
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("libmpv_" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$archive = Join-Path $tmp $name

try {
    Write-Host "Downloading $name ..."
    Invoke-WebRequest -Uri $url -OutFile $archive -Headers @{ 'User-Agent' = 'StreamOrchestrator' }

    # Locate 7-Zip.
    $sevenZip = @(
        'C:\Program Files\7-Zip\7z.exe',
        'C:\Program Files (x86)\7-Zip\7z.exe',
        (Get-Command 7z -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if (-not $sevenZip) {
        throw '7-Zip not found. Install it (winget install 7zip.7zip) and re-run.'
    }

    Write-Host 'Extracting libmpv-2.dll ...'
    & $sevenZip e $archive 'libmpv-2.dll' "-o$libsDir" -y | Out-Null

    if (-not (Test-Path $dllPath)) { throw 'Extraction failed: libmpv-2.dll not found.' }
    Write-Host "Done: $dllPath" -ForegroundColor Green
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
