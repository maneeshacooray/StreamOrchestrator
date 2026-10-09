#requires -Version 5.1
<#
.SYNOPSIS
    Produces a self-contained Release build of StreamOrchestrator in publish/.

.DESCRIPTION
    Self-contained means the target machine needs no .NET runtime installed. The native libmpv
    engine is copied next to the executable (via the project's libs/ content), so end users need
    nothing pre-installed. The installer (installer/StreamOrchestrator.iss) packages this folder.
#>
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot  = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repoRoot 'src/StreamOrchestrator/StreamOrchestrator.csproj'
$outDir    = Join-Path $repoRoot 'publish'

# Ensure the native engine is present before publishing.
& (Join-Path $PSScriptRoot 'fetch-libmpv.ps1')

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

Write-Host "Publishing self-contained $Configuration build..." -ForegroundColor Cyan
dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $outDir

$exe = Join-Path $outDir 'StreamOrchestrator.exe'
$dll = Join-Path $outDir 'libmpv-2.dll'
if (-not (Test-Path $exe)) { throw "Publish failed: $exe not found." }
if (-not (Test-Path $dll)) { throw "Publish failed: libmpv-2.dll missing from output." }

Write-Host "Published to $outDir" -ForegroundColor Green
