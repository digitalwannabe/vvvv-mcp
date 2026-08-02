<#
.SYNOPSIS
    Indexes all vvvv gamma help patches to build a per-node example lookup.

.DESCRIPTION
    Scans installed vvvv packages for help patches (*.vl files in help/ and
    _help/ directories), parses their XML to extract node names and categories,
    and builds two output files:

      output/help_index.json     – machine-readable index (per-node: which files use it)
      output/vl-help-examples.md – human/AI-readable knowledge document listing examples

    This enables the MCP to answer "where is node X used?" and "show me an example
    of ForEach/If/Reactive/etc." by linking to real help patches.

.PARAMETER VvvvPackagesDir
    Root directory that contains installed vvvv NuGet packages.
    Defaults to common locations (vvvv AppData, packs-community in this repo).

.PARAMETER OutputDir
    Directory to write output files. Defaults to VVVVNodeAnalyzer/output/.

.PARAMETER MaxFilesPerNode
    Maximum help files to list per node in the knowledge doc. Default: 5.

.PARAMETER MaxNodes
    Maximum unique nodes to include in the knowledge doc. Default: 500.

.EXAMPLE
    # Index the community packs already downloaded by install-community-packs.ps1
    ./scripts/index-help-patches.ps1

.EXAMPLE
    # Index from a specific vvvv installation
    ./scripts/index-help-patches.ps1 -VvvvPackagesDir "C:\Users\Me\AppData\Roaming\vvvv\gamma\packages"
#>
[CmdletBinding()]
param(
    [string[]] $VvvvPackagesDirs = @(),
    [string]   $OutputDir        = "",
    [int]      $MaxFilesPerNode  = 5,
    [int]      $MaxNodes         = 800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────────────────────

$scriptFile = $MyInvocation.MyCommand.Path
$repoRoot   = if ($scriptFile) { Split-Path (Split-Path $scriptFile -Parent) -Parent } else { Get-Location }

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "VVVVNodeAnalyzer\output"
}

if ($VvvvPackagesDirs.Count -eq 0) {
    # Common locations -- try all that exist
    $candidates = @(
        (Join-Path $repoRoot "packs-community"),                          # this repo
        "$env:APPDATA\vvvv\gamma\packages",                               # vvvv AppData
        "$env:LOCALAPPDATA\vvvv\gamma\packages",                          # alternate
        "C:\Program Files\vvvv\vvvv_gamma\lib\packs",                    # global install
        "C:\vvvv\vvvv_gamma\lib\packs"                                    # portable install
    )
    $VvvvPackagesDirs = $candidates | Where-Object { Test-Path $_ }
}

if ($VvvvPackagesDirs.Count -eq 0) {
    Write-Warning @"
No vvvv package directories found.

Run one of:
  ./scripts/install-community-packs.ps1    # downloads all packages to packs-community/
  ./scripts/index-help-patches.ps1 -VvvvPackagesDirs "C:\path\to\packages"

Common paths:
  %APPDATA%\vvvv\gamma\packages
  C:\Program Files\vvvv\vvvv_gamma\lib\packs
"@
    return
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host ""
Write-Host "Scanning package directories:"
foreach ($d in $VvvvPackagesDirs) { Write-Host "  $d" }
Write-Host ""

# ── Find all .vl files in help directories ────────────────────────────────────

$helpPatterns = @('help\*.vl', 'help\**\*.vl', '_help\*.vl', '_help\**\*.vl',
                  'Help\*.vl', 'Help\**\*.vl', 'examples\*.vl', 'examples\**\*.vl')

$helpFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
$scannedPacks = [System.Collections.Generic.List[string]]::new()

foreach ($baseDir in $VvvvPackagesDirs) {
    if (-not (Test-Path $baseDir)) { continue }

    # Each subdirectory is a package (e.g. VL.Stride.2025.7.1/)
    $packDirs = Get-ChildItem $baseDir -Directory -ErrorAction SilentlyContinue
    foreach ($packDir in $packDirs) {
        $found = Get-ChildItem $packDir.FullName -Recurse -Filter "*.vl" -File -ErrorAction SilentlyContinue |
            Where-Object {
                $parent = $_.DirectoryName.ToLower()
                $parent -match '[\\/](help|_help|examples)([\\/]|$)'
            }
        if ($found) {
            $scannedPacks.Add($packDir.Name)
            foreach ($f in $found) { [void]$helpFiles.Add($f) }
        }
    }
}

Write-Host "Found $($helpFiles.Count) help files across $($scannedPacks.Count) packages"
if ($helpFiles.Count -eq 0) {
    Write-Warning "No help files found. Check that packages are installed."
    return
}

# ── Parse .vl files to extract node usage ─────────────────────────────────────

# Index: nodeFullName -> list of help file paths
$nodeIndex    = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[string]]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
# Index: helpFilePath -> { package, title, nodeNames[] }
$fileIndex    = [System.Collections.Generic.Dictionary[string, hashtable]]::new()
$parseErrors  = 0
$parsed       = 0

function Get-PackageName([string] $filePath) {
    foreach ($baseDir in $VvvvPackagesDirs) {
        if ($filePath.StartsWith($baseDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            $rel   = $filePath.Substring($baseDir.Length).TrimStart('/\')
            $parts = $rel -split '[\\/]'
            return $parts[0]   # first segment = package directory
        }
    }
    return "unknown"
}

foreach ($f in $helpFiles) {
    try {
        [xml]$xml   = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
        $ns         = @{ p = "property" }
        $nodes      = Select-Xml -Xml $xml -XPath "//p:NodeReference" -Namespace $ns | ForEach-Object { $_.Node }
        $nodeNames  = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

        foreach ($nr in $nodes) {
            # Extract node name from Choice[@Kind='OperationCallFlag' or 'ProcessAppFlag']
            $choices = $nr.SelectNodes("Choice")
            $name    = $null
            $cat     = $nr.GetAttribute("LastCategoryFullName")
            foreach ($ch in $choices) {
                $kind = $ch.GetAttribute("Kind")
                if ($kind -in @("OperationCallFlag","ProcessAppFlag","AdaptiveOperationCallFlag")) {
                    $name = $ch.GetAttribute("Name")
                    break
                }
            }
            if (-not $name) { continue }

            $fullName = if ($cat) { "$cat.$name" } else { $name }
            [void]$nodeNames.Add($fullName)

            if (-not $nodeIndex.ContainsKey($fullName)) {
                $nodeIndex[$fullName] = [System.Collections.Generic.List[string]]::new()
            }
            if ($nodeIndex[$fullName].Count -lt 20) {  # cap at 20 references per node
                $nodeIndex[$fullName].Add($f.FullName)
            }
        }

        $packName = Get-PackageName $f.FullName
        $fileIndex[$f.FullName] = @{
            package = $packName
            name    = $f.Name
            nodes   = @($nodeNames)
        }

        $parsed++
        if ($parsed % 50 -eq 0) { Write-Host "  Parsed $parsed / $($helpFiles.Count)..." }
    } catch {
        $parseErrors++
        Write-Verbose "Parse error: $($f.FullName): $_"
    }
}

Write-Host "Parsed $parsed files ($parseErrors errors)"
Write-Host "Unique nodes referenced: $($nodeIndex.Count)"

# ── Write help_index.json ────────────────────────────────────────────────────

$jsonObj = @{
    generated    = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    totalFiles   = $helpFiles.Count
    totalNodes   = $nodeIndex.Count
    scannedPacks = @($scannedPacks | Sort-Object -Unique)
    nodeIndex    = @{}
    fileIndex    = @{}
}

foreach ($kv in $nodeIndex.GetEnumerator()) {
    $jsonObj.nodeIndex[$kv.Key] = @($kv.Value | Select-Object -Unique)
}
foreach ($kv in $fileIndex.GetEnumerator()) {
    # Store relative path from repo root for portability
    $rel = $kv.Key
    foreach ($d in $VvvvPackagesDirs) {
        if ($kv.Key.StartsWith($d, [System.StringComparison]::OrdinalIgnoreCase)) {
            $rel = "[packages]\" + $kv.Key.Substring($d.Length).TrimStart('/\')
            break
        }
    }
    $jsonObj.fileIndex[$rel] = $kv.Value
}

$jsonPath = Join-Path $OutputDir "help_index.json"
$jsonObj | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$jsonKB   = [math]::Round((Get-Item $jsonPath).Length / 1024, 0)
Write-Host ""
Write-Host "  help_index.json  ($jsonKB KB)"

# ── Write vl-help-examples.md ────────────────────────────────────────────────

$mdPath = Join-Path $repoRoot "knowledge\vl-help-examples.md"

# Top-N nodes by number of help file references (most-used = most important)
$topNodes = $nodeIndex.GetEnumerator() |
    Sort-Object { $_.Value.Count } -Descending |
    Select-Object -First $MaxNodes

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("<!-- AUTO-GENERATED by scripts/index-help-patches.ps1 -- DO NOT EDIT MANUALLY -->")
[void]$sb.AppendLine("<!-- Run: ./scripts/index-help-patches.ps1 to regenerate -->")
[void]$sb.AppendLine()
[void]$sb.AppendLine("# vvvv Help Patch Examples Index")
[void]$sb.AppendLine()
[void]$sb.AppendLine("> Auto-indexed from vvvv package help files.")
[void]$sb.AppendLine("> Shows which help patches demonstrate each node -- useful for finding real-world usage patterns.")
[void]$sb.AppendLine("> Full index: ``VVVVNodeAnalyzer/output/help_index.json``")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Summary")
[void]$sb.AppendLine()
[void]$sb.AppendLine("| Stat | Value |")
[void]$sb.AppendLine("|------|-------|")
[void]$sb.AppendLine("| Help files scanned | $($helpFiles.Count) |")
[void]$sb.AppendLine("| Unique nodes found | $($nodeIndex.Count) |")
[void]$sb.AppendLine("| Packages scanned | $($scannedPacks.Count) |")
[void]$sb.AppendLine()

[void]$sb.AppendLine("## Node -> Help File Index")
[void]$sb.AppendLine()
[void]$sb.AppendLine("Format: `NodeFullName` (N help files) -> file1, file2, ...")
[void]$sb.AppendLine()

foreach ($kv in $topNodes) {
    $nodeName   = $kv.Key
    $references = @($kv.Value | Select-Object -Unique)
    $count      = $references.Count

    # Show only file names (not full paths) to keep the doc readable
    $fileNames = $references |
        Select-Object -First $MaxFilesPerNode |
        ForEach-Object { Split-Path $_ -Leaf }
    $more      = if ($count -gt $MaxFilesPerNode) { " *(+$($count - $MaxFilesPerNode) more)*" } else { "" }

    [void]$sb.AppendLine("- ``$nodeName`` ($count) -> " + ($fileNames -join ", ") + $more)
}

[void]$sb.AppendLine()
[void]$sb.AppendLine("---")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Packages Scanned")
[void]$sb.AppendLine()
foreach ($p in ($scannedPacks | Sort-Object -Unique)) {
    [void]$sb.AppendLine("- $p")
}

Set-Content -LiteralPath $mdPath -Value $sb.ToString() -Encoding UTF8
$mdKB = [math]::Round((Get-Item $mdPath).Length / 1024, 0)

Write-Host "  vl-help-examples.md  ($mdKB KB, $(@($topNodes).Count) nodes)"
Write-Host ""
Write-Host "Done! To add to the knowledge base, run:"
Write-Host "  ./scripts/build-knowledge.ps1"
Write-Host ""
Write-Host "To use: MCP tool `search_knowledge query='ForEach example'` will find matching help patches."
