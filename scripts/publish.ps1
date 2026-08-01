<#
.SYNOPSIS
    Builds and packs vvvv-mcp locally, then installs it as a global tool for testing.
    NuGet.org publishing is handled by GitHub Actions (.github/workflows/publish.yml).

.PARAMETER Version
    Override the version in VvvvMcp.csproj (e.g. "0.3.0-preview").
    Leave empty to use the version already in the csproj.

.PARAMETER SkipBuild
    Skip dotnet build (use if already built in Release).

.EXAMPLE
    ./scripts/publish.ps1                      # build, pack, local install
    ./scripts/publish.ps1 -Version 0.3.0       # bump version, build, pack, local install

.NOTES
    To publish to NuGet.org:
      git tag v0.3.0
      git push --tags
    GitHub Actions will handle the rest via Trusted Publishing.
#>
[CmdletBinding()]
param(
    [string] $Version  = "",
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$csproj   = Join-Path $repoRoot "src\VvvvMcp\VvvvMcp.csproj"
$nupkgDir = Join-Path $repoRoot "nupkg"

# ---- Optionally bump version -----------------------------------------------

if ($Version) {
    $xml = [xml](Get-Content $csproj)
    $node = $xml.SelectSingleNode("//Version")
    if (-not $node) { Write-Error "<Version> not found in $csproj"; return }
    $old = $node.InnerText
    $node.InnerText = $Version
    $xml.Save($csproj)
    Write-Host "Version: $old -> $Version"
}

$xml = [xml](Get-Content $csproj)
$currentVersion = $xml.SelectSingleNode("//Version").InnerText
Write-Host "Packaging vvvv-mcp $currentVersion"
Write-Host ""

# ---- Build -----------------------------------------------------------------

if (-not $SkipBuild) {
    Write-Host "Building..."
    dotnet build $csproj -c Release 2>&1 | Select-Object -Last 5
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; return }
}

# ---- Pack ------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $nupkgDir | Out-Null
Get-ChildItem $nupkgDir -Filter "vvvv-mcp.*.nupkg" | Remove-Item -Force

Write-Host "Packing..."
dotnet pack $csproj -c Release -o $nupkgDir --no-build 2>&1 | Select-Object -Last 4
if ($LASTEXITCODE -ne 0) { Write-Error "Pack failed"; return }

$nupkg = Get-ChildItem $nupkgDir -Filter "vvvv-mcp.*.nupkg" | Select-Object -First 1
$sizeMB = [math]::Round($nupkg.Length / 1MB, 1)
Write-Host "Package: $($nupkg.Name) ($sizeMB MB)"

# ---- Local install test ----------------------------------------------------

Write-Host ""
Write-Host "Installing locally..."
dotnet tool uninstall -g vvvv-mcp 2>$null
dotnet tool install -g --add-source $nupkgDir vvvv-mcp
if ($LASTEXITCODE -ne 0) { Write-Error "Local install failed"; return }

Write-Host ""
Write-Host "Installed: $(vvvv-mcp --version)"
Write-Host ""
Write-Host "Run 'vvvv-mcp --setup' to configure MCP clients."
Write-Host ""
Write-Host "To publish to NuGet.org, push a version tag:"
Write-Host "  git tag v$currentVersion"
Write-Host "  git push --tags"
