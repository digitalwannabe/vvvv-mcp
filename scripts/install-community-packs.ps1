<#
.SYNOPSIS
    Downloads all vvvv packages from NuGet.org and generates the MCP node catalog.
    No local vvvv installation required.

.DESCRIPTION
    Downloads two sets of packages from NuGet.org:

    1. Libraries.xml packages  -- the vvvv community catalog
       (https://github.com/vvvv/PublicContent/blob/master/Libraries.xml)

    2. vvvv core/infrastructure packages -- ship with every vvvv install but are
       also published to NuGet.org (VL.CoreLib, VL.Stride, VL.Core, VL.HDE, etc.)

    All packages are extracted to <OutputDir>/<PackageName>.<Version>/ and the
    VVVVNodeAnalyzer is then run against the output directory to produce
    VVVVNodeAnalyzer/vvvv_nodes_mcp.json.

    The Hidden category in Libraries.xml is excluded.
    VL.HDE and *_HDE_* editor-internal packages are downloaded but excluded from
    the analysis (they contain editor-only nodes, not user-facing API).

.PARAMETER OutputDir
    Directory where packages are extracted.
    Defaults to <repo-root>/packs-community.

.PARAMETER LibrariesXmlUrl
    URL of Libraries.xml. Defaults to vvvv/PublicContent on GitHub.

.PARAMETER Force
    Re-download packages that already exist in OutputDir.

.PARAMETER SkipAnalysis
    Download packages but do not run the VVVVNodeAnalyzer afterward.

.EXAMPLE
    ./scripts/install-community-packs.ps1

.EXAMPLE
    ./scripts/install-community-packs.ps1 -Force

.EXAMPLE
    ./scripts/install-community-packs.ps1 -SkipAnalysis
#>
[CmdletBinding()]
param(
    [string] $OutputDir       = "",
    [string] $LibrariesXmlUrl = "https://raw.githubusercontent.com/vvvv/PublicContent/master/Libraries.xml",
    [switch] $Force,
    [switch] $SkipAnalysis
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---- Paths ------------------------------------------------------------------

$scriptDir = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { (Get-Location).Path }

if (-not $OutputDir) { $OutputDir = Join-Path $scriptDir "packs-community" }
$analyzerProject = Join-Path $scriptDir "VVVVNodeAnalyzer\VVVVNodeAnalyzer.csproj"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "Output dir  : $OutputDir"
Write-Host "No local vvvv install required -- all packages are on NuGet.org"
Write-Host ""

# ---- Infrastructure packages that ship with vvvv but are NOT in Libraries.xml
# All confirmed to be available on NuGet.org.
$corePackages = @(
    "VL.App.Console"
    "VL.App.WindowsForms"
    "VL.AppServices"
    "VL.Core"
    "VL.Core.Commands"
    "VL.Core.Skia"
    "VL.CoreLib"               # also in Libraries.xml (Unsorted), listed here for clarity
    "VL.CoreLib.Windows"
    "VL.EditingFramework"
    "VL.EditingFramework.Skia"
    "VL.Fundamentals"
    "VL.FuzzySearch"
    "VL.HDE"                   # editor-internal; downloaded but excluded from analysis
    "VL.LogView"
    "VL.Serialization.FSPickler"
    "VL.Serialization.MessagePack"
    "VL.Serialization.Raw"
    "VL.Stride"
    "VL.Stride.DefaultAssets"
    "VL.Stride.HDE"
    "VL.Stride.Runtime"
    "VL.Stride.TextureFX"
    "VL.Stride.Windows"
    "VL.TPL.Dataflow"
    "VL.Video"
)

# ---- 1. Fetch Libraries.xml -------------------------------------------------

Write-Host "Fetching Libraries.xml from GitHub..."
try {
    $xmlContent = (Invoke-WebRequest $LibrariesXmlUrl -UseBasicParsing -TimeoutSec 20).Content
    Write-Host "  OK"
} catch {
    Write-Error "Could not fetch Libraries.xml: $_"
    return
}

[xml]$libXml = $xmlContent

function Collect-NugetIds([System.Xml.XmlElement] $node) {
    $ids = New-Object System.Collections.Generic.List[string]
    if ($node.GetAttribute("title") -eq "Hidden") { return ,$ids }
    foreach ($child in $node.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($child.Name -eq "Lib") {
            $nuget = $child.GetAttribute("nuget")
            if ($nuget) { $ids.Add($nuget) }
        } elseif ($child.Name -eq "Category") {
            $ids.AddRange((Collect-NugetIds $child))
        }
    }
    return ,$ids
}

$communityIds = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($cat in $libXml.DocumentElement.ChildNodes) {
    if ($cat.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
    $catIds = Collect-NugetIds $cat
    foreach ($id in $catIds) { [void]$communityIds.Add($id) }
}

Write-Host ("Found {0} unique packages in Libraries.xml" -f $communityIds.Count)

# ---- 2. Build the full deduplicated download list ---------------------------

$allToDownload = New-Object System.Collections.Generic.List[string]
$seen = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)

# Core/infrastructure packages first
foreach ($pkg in $corePackages) {
    if ($seen.Add($pkg)) { $allToDownload.Add($pkg) }
}

# Community packages from Libraries.xml
foreach ($pkg in ($communityIds | Sort-Object)) {
    if ($seen.Add($pkg)) { $allToDownload.Add($pkg) }
}

Write-Host ("Total packages to download: {0} ({1} core + {2} community)" -f `
    $allToDownload.Count,
    ($corePackages | Where-Object { $seen.Contains($_) } | Measure-Object).Count,
    $communityIds.Count)
Write-Host ""

# ---- 3. NuGet download helpers ----------------------------------------------

function Get-PackageInfo([string] $packageId) {
    $regUrl = "https://api.nuget.org/v3/registration5-semver1/$($packageId.ToLower())/index.json"
    try {
        $reg = Invoke-RestMethod $regUrl -TimeoutSec 15 -ErrorAction Stop
        $latestVer = $null; $downloadUrl = $null
        foreach ($page in $reg.items) {
            $items = if ($page.items) { $page.items } else {
                try { (Invoke-RestMethod $page.'@id' -TimeoutSec 15).items } catch { @() }
            }
            foreach ($item in $items) {
                $latestVer   = $item.catalogEntry.version
                $downloadUrl = $item.packageContent
            }
        }
        if ($latestVer -and $downloadUrl) {
            return @{ Found = $true; Version = $latestVer; DownloadUrl = $downloadUrl }
        }
        return @{ Found = $false; Message = "No versions in registration" }
    } catch {
        return @{ Found = $false; Message = $_.Exception.Message }
    }
}

function Install-Package([string] $packageId, [string] $outDir, [bool] $force) {
    $info = Get-PackageInfo $packageId
    if (-not $info.Found) { return @{ Status = "NotFound"; Message = $info.Message } }

    $ver        = $info.Version
    $extractDir = Join-Path $outDir "$packageId.$ver"

    if (-not $force -and (Test-Path $extractDir)) {
        return @{ Status = "Skipped"; Version = $ver }
    }

    $dlDir     = Join-Path $outDir "_downloads"
    $nupkgPath = Join-Path $dlDir "$packageId.$ver.nupkg"
    New-Item -ItemType Directory -Force -Path $dlDir | Out-Null

    try {
        Invoke-WebRequest $info.DownloadUrl -OutFile $nupkgPath -TimeoutSec 90 -UseBasicParsing -ErrorAction Stop
        if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkgPath, $extractDir)
        Remove-Item $nupkgPath -Force -ErrorAction SilentlyContinue
        return @{ Status = "OK"; Version = $ver }
    } catch {
        return @{ Status = "Error"; Message = $_.Exception.Message }
    }
}

# ---- 4. Download all packages -----------------------------------------------

$total     = $allToDownload.Count
$okCount   = 0; $skipCount = 0; $failCount = 0
$failList  = New-Object System.Collections.Generic.List[object]

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$idx = 0
foreach ($pkgId in $allToDownload) {
    $idx++
    Write-Host -NoNewline ("  [{0}/{1}] {2} ... " -f $idx, $total, $pkgId)

    $r = Install-Package $pkgId $OutputDir $Force.IsPresent
    switch ($r.Status) {
        "OK"       { $okCount++;   Write-Host "OK $($r.Version)" }
        "Skipped"  { $skipCount++; Write-Host "already present $($r.Version)" }
        "NotFound" { $failCount++; $failList.Add([PSCustomObject]@{Id=$pkgId;Msg=$r.Message}); Write-Host "NOT FOUND -- $($r.Message)" }
        "Error"    { $failCount++; $failList.Add([PSCustomObject]@{Id=$pkgId;Msg=$r.Message}); Write-Host "ERROR -- $($r.Message)" }
    }
}
$sw.Stop()

# Remove temp downloads dir
$dlDir = Join-Path $OutputDir "_downloads"
if (Test-Path $dlDir) { Remove-Item $dlDir -Recurse -Force -ErrorAction SilentlyContinue }

# ---- 5. Summary -------------------------------------------------------------

Write-Host ""
Write-Host ("=" * 60)
Write-Host ("DOWNLOAD SUMMARY  ({0})" -f $sw.Elapsed.ToString('mm\:ss'))
Write-Host ("=" * 60)
Write-Host ("  Downloaded  : {0}" -f $okCount)
Write-Host ("  Skipped     : {0}  (already present, use -Force to refresh)" -f $skipCount)
Write-Host ("  Not found   : {0}" -f $failCount)
Write-Host ""

if ($failList.Count -gt 0) {
    Write-Host "Packages not found on NuGet.org:"
    foreach ($f in $failList | Sort-Object Id) {
        Write-Host ("  {0}  --  {1}" -f $f.Id, $f.Msg)
    }
    Write-Host ""
}

$installedDirs = @(Get-ChildItem $OutputDir -Directory | Where-Object { $_.Name -ne "_downloads" })
Write-Host ("Total packages in $OutputDir : {0}" -f $installedDirs.Count)
Write-Host ""

# ---- 6. Run VVVVNodeAnalyzer ------------------------------------------------

if (-not $SkipAnalysis) {
    if (-not (Test-Path $analyzerProject)) {
        Write-Warning "VVVVNodeAnalyzer project not found at $analyzerProject -- skipping analysis."
        Write-Warning "Run manually: dotnet run --project VVVVNodeAnalyzer/VVVVNodeAnalyzer.csproj -- batch `"$OutputDir`" VVVVNodeAnalyzer/output"
    } else {
        Write-Host "Running VVVVNodeAnalyzer on $OutputDir ..."
        Write-Host ""
        $analyzerOutput = Join-Path $scriptDir "VVVVNodeAnalyzer\output"
        & dotnet run --project $analyzerProject -- batch $OutputDir $analyzerOutput

        if ($LASTEXITCODE -eq 0) {
            # Copy to output/
            $outCopy = Join-Path $scriptDir "output"
            New-Item -ItemType Directory -Force -Path $outCopy | Out-Null
            Copy-Item "$analyzerOutput\vvvv_nodes_mcp.json" "$outCopy\vvvv_nodes_mcp.json" -Force
            Copy-Item "$analyzerOutput\vvvv_nodes_mcp.md"   "$outCopy\vvvv_nodes_mcp.md"   -Force
            Write-Host ""
            Write-Host "Catalog copied to output/"
        } else {
            Write-Warning "Analyzer exited with code $LASTEXITCODE"
        }
    }
} else {
    Write-Host "Skipping analysis (-SkipAnalysis). To run manually:"
    Write-Host ("  dotnet run --project VVVVNodeAnalyzer/VVVVNodeAnalyzer.csproj -- batch `"{0}`" VVVVNodeAnalyzer" -f $OutputDir)
}
