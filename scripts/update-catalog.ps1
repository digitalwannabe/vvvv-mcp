<#
.SYNOPSIS
    Re-runs the VVVVNodeAnalyzer against the local vvvv installation to regenerate
    the MCP node catalog (vvvv_nodes_mcp.json).

.DESCRIPTION
    Scans the vvvv gamma packs/ directory for all user-facing packages and
    extracts a comprehensive node catalog used by the vvvv-mcp server.

    Editor-internal packages (VL.HDE and *_HDE_*) are excluded automatically.
    The output is saved to VVVVNodeAnalyzer/vvvv_nodes_mcp.json and output/.

.PARAMETER VvvvInstallDir
    Path to the vvvv gamma installation. If omitted, the script searches
    common install locations automatically.

.EXAMPLE
    ./scripts/update-catalog.ps1

.EXAMPLE
    ./scripts/update-catalog.ps1 -VvvvInstallDir "C:\Program Files\vvvv\vvvv_gamma_7.1-0156-gdf75a792b5-win-x64"
#>
[CmdletBinding()]
param(
    [string] $VvvvInstallDir = ""
)

$ErrorActionPreference = "Stop"

# Locate vvvv install
if (-not $VvvvInstallDir) {
    $candidates = Get-ChildItem "C:\Program Files\vvvv" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if ($candidates) { $VvvvInstallDir = $candidates }
}

if (-not $VvvvInstallDir -or -not (Test-Path $VvvvInstallDir)) {
    Write-Error "vvvv installation not found. Specify -VvvvInstallDir 'C:\Program Files\vvvv\vvvv_gamma_X.Y...'"
    return
}

$packsDir  = Join-Path $VvvvInstallDir "packs"
if (-not (Test-Path $packsDir)) {
    Write-Error "packs/ directory not found at $packsDir"
    return
}

$repoRoot  = Split-Path $PSScriptRoot -Parent
$outputDir = Join-Path $repoRoot "VVVVNodeAnalyzer"
$analyzer  = Join-Path $repoRoot "VVVVNodeAnalyzer\VVVVNodeAnalyzer.csproj"

Write-Host "vvvv install : $VvvvInstallDir"
Write-Host "packs dir    : $packsDir"
Write-Host "output dir   : $outputDir"
Write-Host ""

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet run --project $analyzer -- batch $packsDir $outputDir
$sw.Stop()

if ($LASTEXITCODE -ne 0) {
    Write-Error "Analyzer failed with exit code $LASTEXITCODE"
    return
}

Write-Host ""
Write-Host "Elapsed: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"

# Copy results to output/
$outputCopy = Join-Path $repoRoot "output"
New-Item -ItemType Directory -Force -Path $outputCopy | Out-Null
Copy-Item "$outputDir\vvvv_nodes_mcp.json" "$outputCopy\vvvv_nodes_mcp.json" -Force
Copy-Item "$outputDir\vvvv_nodes_mcp.md"   "$outputCopy\vvvv_nodes_mcp.md" -Force
Write-Host "Catalog copied to output/"
Write-Host ""
Write-Host "Next: restart the vvvv-mcp server so it picks up the new catalog."
Write-Host "  (The server reads the catalog from VVVV_MCP_CATALOG env var at startup.)"
