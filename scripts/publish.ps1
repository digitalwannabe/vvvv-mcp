<#
.SYNOPSIS
    Builds, packs, and publishes vvvv-mcp to NuGet.org.

.DESCRIPTION
    Reads NUGET_API_KEY from .env (gitignored) in the repo root.
    Bumps the version in VvvvMcp.csproj if -Version is supplied.

.PARAMETER Version
    New version string (e.g. "0.3.0"). If omitted, uses the version in VvvvMcp.csproj.

.PARAMETER SkipBuild
    Skip dotnet build (use if already built).

.PARAMETER LocalOnly
    Pack but do not push. Useful for testing the local install.

.EXAMPLE
    ./scripts/publish.ps1

.EXAMPLE
    ./scripts/publish.ps1 -Version 0.3.0

.EXAMPLE
    ./scripts/publish.ps1 -LocalOnly
#>
[CmdletBinding()]
param(
    [string] $Version    = "",
    [switch] $SkipBuild,
    [switch] $LocalOnly
)

$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path $PSScriptRoot -Parent
$csproj    = Join-Path $repoRoot "src\VvvvMcp\VvvvMcp.csproj"
$nupkgDir  = Join-Path $repoRoot "nupkg"
$envFile   = Join-Path $repoRoot ".env"

# ---- Read API key from .env ------------------------------------------------

$apiKey = $null
if (-not $LocalOnly) {
    if (-not (Test-Path $envFile)) {
        Write-Error ".env not found. Copy .env.example to .env and set NUGET_API_KEY."
        return
    }
    foreach ($line in Get-Content $envFile) {
        if ($line -match '^NUGET_API_KEY\s*=\s*(.+)$') {
            $apiKey = $Matches[1].Trim()
        }
    }
    if (-not $apiKey) {
        Write-Error "NUGET_API_KEY is empty in .env. Add your key from https://www.nuget.org/account/apikeys"
        return
    }
}

# ---- Optionally bump version -----------------------------------------------

if ($Version) {
    $xml = [xml](Get-Content $csproj)
    $versionNode = $xml.SelectSingleNode("//Version")
    if (-not $versionNode) { Write-Error "Could not find <Version> in $csproj"; return }
    $oldVer = $versionNode.InnerText
    $versionNode.InnerText = $Version
    $xml.Save($csproj)
    Write-Host "Version bumped: $oldVer -> $Version"
}

# Read final version
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

# Remove any previous version of this package to avoid confusion
Get-ChildItem $nupkgDir -Filter "vvvv-mcp.*.nupkg" | Remove-Item -Force

Write-Host "Packing..."
dotnet pack $csproj -c Release -o $nupkgDir --no-build 2>&1 | Select-Object -Last 4
if ($LASTEXITCODE -ne 0) { Write-Error "Pack failed"; return }

$nupkg = Get-ChildItem $nupkgDir -Filter "vvvv-mcp.*.nupkg" | Select-Object -First 1
if (-not $nupkg) { Write-Error "nupkg not found"; return }

$sizeMB = [math]::Round($nupkg.Length / 1MB, 1)
Write-Host "Package: $($nupkg.Name) ($sizeMB MB)"

# ---- Test local install (always) -------------------------------------------

Write-Host ""
Write-Host "Testing local install..."
dotnet tool uninstall -g vvvv-mcp 2>$null
dotnet tool install -g --add-source $nupkgDir vvvv-mcp 2>&1
if ($LASTEXITCODE -ne 0) { Write-Error "Local install failed"; return }

$toolVer = (vvvv-mcp --version 2>&1).Trim()
Write-Host "Installed: $toolVer"
Write-Host ""

if ($LocalOnly) {
    Write-Host "Done (local only). Run 'vvvv-mcp --setup' to configure clients."
    return
}

# ---- Push to NuGet.org -----------------------------------------------------

Write-Host "Pushing to NuGet.org..."
dotnet nuget push $nupkg.FullName `
    --api-key $apiKey `
    --source "https://api.nuget.org/v3/index.json" `
    --skip-duplicate
if ($LASTEXITCODE -ne 0) { Write-Error "Push failed"; return }

Write-Host ""
Write-Host "Published: https://www.nuget.org/packages/vvvv-mcp/$currentVersion"
Write-Host ""
Write-Host "Users can now install with:"
Write-Host "  dotnet tool install -g vvvv-mcp"
Write-Host "  vvvv-mcp --setup"
