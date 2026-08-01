<#
.SYNOPSIS
    Regenerates the MCP node catalog (vvvv_nodes_mcp.json) from NuGet.

.DESCRIPTION
    Downloads all vvvv packages from NuGet.org (no local vvvv install required),
    runs VVVVNodeAnalyzer against them, and writes the catalog to:
      VVVVNodeAnalyzer/output/vvvv_nodes_mcp.json

    All packages -- including internal vvvv infrastructure (VL.Core, VL.CoreLib,
    VL.Stride, VL.HDE, etc.) -- are published to NuGet.org, so the catalog is
    always complete without a local install.

    Packages already present in packs-community/ are skipped automatically.
    Use -Force to re-download everything.

.PARAMETER Force
    Re-download all packages even if already present in packs-community/.

.EXAMPLE
    ./scripts/update-catalog.ps1

.EXAMPLE
    ./scripts/update-catalog.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch] $Force
)

$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path $PSScriptRoot -Parent
$analyzerProj  = Join-Path $repoRoot "VVVVNodeAnalyzer\VVVVNodeAnalyzer.csproj"
$analyzerOutput = Join-Path $repoRoot "VVVVNodeAnalyzer\output"
$communityDir  = Join-Path $repoRoot "packs-community"
$installScript = Join-Path $PSScriptRoot "install-community-packs.ps1"

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ---- Step 1: Download / update packages from NuGet --------------------------

$forceArg = if ($Force) { @("-Force") } else { @() }
& powershell -ExecutionPolicy Bypass -File $installScript -SkipAnalysis @forceArg
if ($LASTEXITCODE -ne 0) { Write-Error "Package download failed (exit $LASTEXITCODE)"; return }

# ---- Step 2: Run VVVVNodeAnalyzer -------------------------------------------

Write-Host ""
Write-Host "Running VVVVNodeAnalyzer..."
& dotnet run --project $analyzerProj -- batch $communityDir $analyzerOutput
if ($LASTEXITCODE -ne 0) { Write-Error "Analyzer failed (exit $LASTEXITCODE)"; return }

# ---- Step 3: Summary --------------------------------------------------------

$sw.Stop()

$json = Get-Content "$analyzerOutput\vvvv_nodes_mcp.json" -Raw | ConvertFrom-Json
Write-Host ""
Write-Host ("Elapsed  : {0}" -f $sw.Elapsed.ToString('mm\:ss'))
Write-Host ("Catalog  : {0} nodes, {1} categories" -f $json.totalNodes, $json.categories.Count)
Write-Host "Output   : $analyzerOutput"
Write-Host ""
Write-Host "Restart the vvvv-mcp server to pick up the new catalog."
