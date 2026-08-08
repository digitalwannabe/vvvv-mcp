<#
.SYNOPSIS
    Builds the vvvv-mcp knowledge base from git submodules.

.DESCRIPTION
    Reads from two submodules and generates knowledge/*.md files for KnowledgeService:
        knowledge/The-Gray-Book/        -> gray-book-*.md
        knowledge/tebjan-vvvv-skills/   -> vvvv-*.md / vl-*.md

    Files NOT touched (manually maintained):
        knowledge/vvvv-concepts.md
        knowledge/vvvv-packages.md

    Re-run after: git submodule update --remote --merge

.PARAMETER RepoRoot
    Root of the vvvv-mcp repo. Defaults to parent of this script's directory.

.EXAMPLE
    ./scripts/build-knowledge.ps1

.EXAMPLE
    ./scripts/build-knowledge.ps1 -Verbose
#>
[CmdletBinding()]
param(
    [string] $RepoRoot     = "",
    [string] $KnowledgeDir = "",
    [string] $GrayBookDir  = "",
    [string] $SkillsDir    = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve repo root robustly (PSScriptRoot is unreliable when called from nested shells)
if (-not $RepoRoot) {
    $scriptFile = $MyInvocation.MyCommand.Path
    if ($scriptFile) {
        $RepoRoot = Split-Path (Split-Path $scriptFile -Parent) -Parent
    } else {
        $RepoRoot = (Get-Location).Path
    }
}

if (-not $KnowledgeDir) { $KnowledgeDir = Join-Path $RepoRoot "knowledge" }
if (-not $GrayBookDir)  { $GrayBookDir  = Join-Path $KnowledgeDir "The-Gray-Book" }
if (-not $SkillsDir)    { $SkillsDir    = Join-Path $KnowledgeDir "tebjan-vvvv-skills" }

# Validation
foreach ($item in @(@{Path=$GrayBookDir; Name="The-Gray-Book"}, @{Path=$SkillsDir; Name="tebjan-vvvv-skills"})) {
    if (-not (Test-Path $item.Path)) {
        Write-Error "$($item.Name) not found at $($item.Path). Run: git submodule update --init --recursive"
        return
    }
    $count = (Get-ChildItem $item.Path -Recurse -Filter "*.md" -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($count -eq 0) {
        Write-Warning "$($item.Name) appears empty. Run: git submodule update --init --recursive"
    }
    Write-Verbose "$($item.Name) found ($count md files)"
}

New-Item -ItemType Directory -Force -Path $KnowledgeDir | Out-Null

# Files the script must NEVER overwrite (vvvv-packages.md is now generated from Libraries.xml)
$ManualFiles = @("vl-quickref.md", "vl-patterns.md", "vl-building-blocks.md", "vl-common-graphs.md", "vl-project-architecture.md", "vvvv-internals-advanced.md")

# Replace markdown image references with text markers
function Convert-ImageRefs([string] $content) {
    $lines = $content -split "`n"
    $result = foreach ($line in $lines) {
        $converted = [System.Text.RegularExpressions.Regex]::Replace(
            $line,
            '!\[([^\]]*)\]\(([^)]+)\)',
            {
                param($m)
                $alt   = $m.Groups[1].Value.Trim()
                $fname = Split-Path $m.Groups[2].Value.Trim() -Leaf
                if ($alt) {
                    "> [IMAGE: $fname -- ""$alt""] (visual; see https://thegraybook.vvvv.org)"
                } else {
                    "> [IMAGE: $fname] (visual; see https://thegraybook.vvvv.org)"
                }
            }
        )
        $converted
    }
    return $result -join "`n"
}

# Strip YAML frontmatter (--- ... ---)
function Remove-Frontmatter([string] $content) {
    $trimmed = $content.TrimStart()
    if (-not $trimmed.StartsWith("---")) { return $content }
    $lines = $trimmed -split "`n"
    $end = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].TrimEnd() -eq "---") { $end = $i; break }
    }
    if ($end -lt 0) { return $content }
    return ($lines[($end + 1)..($lines.Count - 1)] -join "`n").TrimStart()
}

# Extract 'description:' value from YAML frontmatter
function Get-FrontmatterDescription([string] $content) {
    $m = [System.Text.RegularExpressions.Regex]::Match($content, '(?m)^description:\s*[''"]?(.+?)[''"]?\s*$')
    if ($m.Success) { return $m.Groups[1].Value.Trim().Trim('"').Trim("'") }
    return ""
}

$generatedFiles = [System.Collections.Generic.List[string]]::new()

# --- Gray Book sections ---
$header = "Note: Gray Book images are replaced with [IMAGE: ...] markers. See https://thegraybook.vvvv.org/ for originals."

$grayBookSections = [ordered]@{
    "gray-book-language"        = @{ Dir = "reference/language";        Title = "Language (VL)" }
    "gray-book-extending"       = @{ Dir = "reference/extending";       Title = "Extending vvvv (Nodes, Shaders, Libraries)" }
    "gray-book-libraries"       = @{ Dir = "reference/libraries";       Title = "Libraries" }
    "gray-book-hde"             = @{ Dir = "reference/hde";             Title = "Development Environment (HDE)" }
    "gray-book-best-practice"   = @{ Dir = "reference/best-practice";   Title = "Best Practice" }
    "gray-book-getting-started" = @{ Dir = "reference/getting-started"; Title = "Getting Started" }
    "gray-book-introduction"    = @{ Dir = "introduction";              Title = "Explanations and Introduction" }
}

foreach ($outputName in $grayBookSections.Keys) {
    $section     = $grayBookSections[$outputName]
    $sectionDir  = Join-Path $GrayBookDir $section.Dir
    $outputPath  = Join-Path $KnowledgeDir "$outputName.md"

    if (-not (Test-Path $sectionDir)) {
        Write-Warning "Section not found, skipping: $sectionDir"
        continue
    }

    $mdFiles = Get-ChildItem -Path $sectionDir -Filter "*.md" -Recurse |
               Where-Object { $_.Name -ne "toc.md" } |
               Sort-Object FullName

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("<!-- AUTO-GENERATED by scripts/build-knowledge.ps1 -- DO NOT EDIT MANUALLY -->")
    [void]$sb.AppendLine("<!-- Source: The-Gray-Book submodule / $($section.Dir) -->")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("# Gray Book -- $($section.Title)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("> Source: https://thegraybook.vvvv.org/ (CC-licensed)")
    [void]$sb.AppendLine("> $header")
    [void]$sb.AppendLine()

    foreach ($f in $mdFiles) {
        $relPath = $f.FullName.Replace($sectionDir, "").TrimStart("/\").Replace("\", "/")
        [void]$sb.AppendLine("---")
        [void]$sb.AppendLine("<!-- page: $relPath -->")
        [void]$sb.AppendLine()
        $raw       = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
        $processed = Convert-ImageRefs $raw
        [void]$sb.AppendLine($processed.Trim())
        [void]$sb.AppendLine()
    }

    Set-Content -LiteralPath $outputPath -Value $sb.ToString() -Encoding UTF8

    $kb = [math]::Round((Get-Item $outputPath).Length / 1024, 1)
    Write-Host "  [gray-book] $outputName.md  ($($mdFiles.Count) pages, ${kb} KB)"
    $generatedFiles.Add("$outputName.md")
}

# --- tebjan vvvv-skills ---
$skillOutputMap = @{
    "vvvv-fileformat" = "vl-file-format.md"   # backward compat with KnowledgeResources
}

$skillsRoot = Join-Path $SkillsDir "skills"
if (-not (Test-Path $skillsRoot)) {
    Write-Warning "Skills root not found: $skillsRoot"
} else {
    $skillDirs = Get-ChildItem $skillsRoot -Directory | Sort-Object Name

    foreach ($skillDir in $skillDirs) {
        $skillName  = $skillDir.Name
        $outputFile = if ($skillOutputMap.ContainsKey($skillName)) {
            $skillOutputMap[$skillName]
        } else {
            "$skillName.md"
        }
        $outputPath = Join-Path $KnowledgeDir $outputFile

        if ($ManualFiles -contains $outputFile) {
            Write-Verbose "Skipping manually maintained: $outputFile"
            continue
        }

        $mainSkillPath = Join-Path $skillDir.FullName "SKILL.md"
        if (-not (Test-Path $mainSkillPath)) {
            Write-Warning "No SKILL.md in $($skillDir.FullName), skipping"
            continue
        }

        $extraMd = @(Get-ChildItem $skillDir.FullName -Filter "*.md" |
                   Where-Object { $_.Name -ne "SKILL.md" } |
                   Sort-Object Name)

        $sb = New-Object System.Text.StringBuilder
        [void]$sb.AppendLine("<!-- AUTO-GENERATED by scripts/build-knowledge.ps1 -- DO NOT EDIT MANUALLY -->")
        [void]$sb.AppendLine("<!-- Source: tebjan-vvvv-skills submodule / skills/$skillName -->")
        [void]$sb.AppendLine()

        $rawSkill = Get-Content -LiteralPath $mainSkillPath -Raw -Encoding UTF8
        $desc     = Get-FrontmatterDescription $rawSkill
        if ($desc) {
            [void]$sb.AppendLine("<!-- description: $desc -->")
            [void]$sb.AppendLine()
        }

        $body = Remove-Frontmatter $rawSkill
        [void]$sb.AppendLine($body.Trim())
        [void]$sb.AppendLine()

        foreach ($extra in $extraMd) {
            $extraContent = Get-Content -LiteralPath $extra.FullName -Raw -Encoding UTF8
            [void]$sb.AppendLine("---")
            [void]$sb.AppendLine("<!-- supplementary: $($extra.Name) -->")
            [void]$sb.AppendLine()
            [void]$sb.AppendLine($extraContent.Trim())
            [void]$sb.AppendLine()
        }

        Set-Content -LiteralPath $outputPath -Value $sb.ToString() -Encoding UTF8

        $kb     = [math]::Round((Get-Item $outputPath).Length / 1024, 1)
        $extras = if ($extraMd.Count -gt 0) { " + $($extraMd.Count) extra files" } else { "" }
        Write-Host "  [skill]     $outputFile  (${kb} KB$extras)"
        $generatedFiles.Add($outputFile)
    }
}

# --- Libraries.xml -> vvvv-packages.md ---
# Fetched from vvvv/PublicContent on GitHub — run periodically to pick up new packages

$librariesXmlUrl  = "https://raw.githubusercontent.com/vvvv/PublicContent/master/Libraries.xml"
$packagesOutput   = Join-Path $KnowledgeDir "vvvv-packages.md"

Write-Host "  [fetch]     Downloading Libraries.xml from GitHub..."
$xmlContent = $null
try {
    $xmlContent = (Invoke-WebRequest -Uri $librariesXmlUrl -UseBasicParsing -TimeoutSec 30).Content
} catch {
    Write-Warning "Could not fetch Libraries.xml: $_"
    Write-Warning "vvvv-packages.md will not be regenerated this run."
}

if ($xmlContent) {
    [xml]$libXml = $xmlContent

    # Collect all unique nuget IDs (deduplicate across categories)
    $allNugets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    # Recursive function to render a Category node
    function Render-Category([System.Xml.XmlElement] $cat, [int] $depth) {
        $heading = "#" * ($depth + 2)
        $lines   = [System.Collections.Generic.List[string]]::new()
        $title   = $cat.GetAttribute("title")
        if ($title -and $title -ne "Hidden") {
            $lines.Add("$heading $title")
            $lines.Add("")
        }

        foreach ($child in $cat.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }

            if ($child.Name -eq "Lib") {
                $nuget   = $child.GetAttribute("nuget")
                $builtin = $child.GetAttribute("builtin")
                if (-not $nuget) { continue }
                [void]$allNugets.Add($nuget)

                $tag  = if ($builtin -eq "true") { " *(builtin)*" } else { "" }
                $links = @()
                foreach ($lnk in $child.SelectNodes("Link")) {
                    $ltitle = $lnk.GetAttribute("title")
                    $lurl   = $lnk.GetAttribute("link")
                    if ($ltitle -and $lurl) { $links += "[$ltitle]($lurl)" }
                }
                $linkStr = if ($links.Count -gt 0) { " -- " + ($links -join ", ") } else { "" }
                $lines.Add("- ``$nuget``$tag$linkStr")
            } elseif ($child.Name -eq "Category") {
                $catTitle = $child.GetAttribute("title")
                if ($catTitle -eq "Hidden") { continue }
                $lines.Add("")
                $lines.AddRange((Render-Category $child ($depth + 1)))
            }
        }
        return ,$lines
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("<!-- AUTO-GENERATED by scripts/build-knowledge.ps1 -- DO NOT EDIT MANUALLY -->")
    [void]$sb.AppendLine("<!-- Source: https://github.com/vvvv/PublicContent/blob/master/Libraries.xml -->")
    [void]$sb.AppendLine("<!-- Re-generate: ./scripts/build-knowledge.ps1 (fetches latest from GitHub) -->")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("# vvvv gamma - Package Library Reference")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("> Source: [vvvv/PublicContent Libraries.xml](https://github.com/vvvv/PublicContent/blob/master/Libraries.xml)")
    [void]$sb.AppendLine("> This is the official curated list of vvvv packages, updated by the vvvv team.")
    [void]$sb.AppendLine("> Full package catalog: https://vvvv.org/packs")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## How to Add a Package")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("In a `.vl` document, add as a direct child of `<Document>` (NOT inside `<Patch>`):")
    [void]$sb.AppendLine("``````xml")
    $exampleLine = '<NugetDependency Id="{22-char-base62-id}" Location="VL.PackageName" Version="2025.7.*" />'
    [void]$sb.AppendLine($exampleLine)
    [void]$sb.AppendLine("``````")
    [void]$sb.AppendLine("Or use: **Edit > Manage NuGets** inside vvvv.")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## Package Categories")
    [void]$sb.AppendLine()

    $root = $libXml.DocumentElement
    foreach ($cat in $root.SelectNodes("Category")) {
        $catTitle = $cat.GetAttribute("title")
        if ($catTitle -eq "Hidden") { continue }
        $catLines = Render-Category $cat 0
        foreach ($line in $catLines) { [void]$sb.AppendLine($line) }
        [void]$sb.AppendLine()
    }

    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## Core / Built-in Packages")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("These ship with vvvv and are available without extra install:")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| Package | Purpose |")
    [void]$sb.AppendLine("|---------|---------|")
    [void]$sb.AppendLine("| ``VL.CoreLib`` | Core nodes: Math, Collections, IO, Animation, Reactive, 2D/3D, Color, Control, Primitive, System |")
    [void]$sb.AppendLine("| ``VL.Stride`` | 3D engine (Stride): SceneWindow, RootScene, Entity, Models, Materials, Cameras, Lights, Shaders |")
    [void]$sb.AppendLine("| ``VL.Skia`` | 2D vector graphics (Skia engine) |")
    [void]$sb.AppendLine("| ``VL.Audio`` | Audio playback, DSP, NAudio integration |")
    [void]$sb.AppendLine("| ``VL.IO.Redis`` | Redis key-value store client |")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## Total Packages in Catalog")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("$($allNugets.Count) unique packages (excluding Hidden category).")

    Set-Content -LiteralPath $packagesOutput -Value $sb.ToString() -Encoding UTF8
    $kb = [math]::Round((Get-Item $packagesOutput).Length / 1024, 1)
    Write-Host "  [fetch]     vvvv-packages.md  ($($allNugets.Count) packages, ${kb} KB)"
    $generatedFiles.Add("vvvv-packages.md")
}

# --- knowledge/templates/ -> vl-templates.md ---
# Embeds all template files with descriptions so the knowledge search can find them.

$templatesDir   = Join-Path $KnowledgeDir "templates"
$templatesOutput = Join-Path $KnowledgeDir "vl-templates.md"

if (Test-Path $templatesDir) {
    $templateFiles = Get-ChildItem $templatesDir -Recurse -File |
        Where-Object { $_.Extension -in @('.vl','.cs','.csproj','.sdsl','.hlsl') } |
        Sort-Object FullName

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("<!-- AUTO-GENERATED by scripts/build-knowledge.ps1 -- DO NOT EDIT MANUALLY -->")
    [void]$sb.AppendLine("<!-- Source: knowledge/templates/ -->")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("# vvvv Template Files")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("> These are the canonical starting-point templates for creating vvvv gamma files.")
    [void]$sb.AppendLine("> Use `list_templates` and `get_template` MCP tools to access them at runtime.")
    [void]$sb.AppendLine("> Use ``create_shader`` or ``create_csharp_plugin`` -- they use these templates automatically.")
    [void]$sb.AppendLine()

    # Sections
    $sections = [ordered]@{
        "vl"     = "VL Patch Templates (.vl)"
        "csharp" = "C# Node Templates (.cs / .csproj)"
        "sdsl"   = "SDSL Shader Templates (.sdsl)"
    }

    foreach ($cat in $sections.Keys) {
        $catFiles = $templateFiles | Where-Object {
            $rel = $_.FullName.Replace($templatesDir, '').TrimStart('/\')
            $rel.StartsWith($cat + '\') -or $rel.StartsWith($cat + '/')
        }
        if (-not $catFiles) { continue }

        [void]$sb.AppendLine("## $($sections[$cat])")
        [void]$sb.AppendLine()

        foreach ($f in $catFiles) {
            $rel  = $f.FullName.Replace($templatesDir, '').TrimStart('/\').Replace('\','/')
            $ext  = $f.Extension.ToLower()
            $lang = switch ($ext) {
                '.vl'     { 'xml' }
                '.cs'     { 'csharp' }
                '.csproj' { 'xml' }
                '.sdsl'   { 'hlsl' }
                '.hlsl'   { 'hlsl' }
                default   { 'text' }
            }

            [void]$sb.AppendLine("### ``$rel``")
            [void]$sb.AppendLine()
            [void]$sb.AppendLine("``````$lang")
            $rawContent = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
            [void]$sb.AppendLine($rawContent.TrimEnd())
            [void]$sb.AppendLine("``````")
            [void]$sb.AppendLine()
        }
    }

    Set-Content -LiteralPath $templatesOutput -Value $sb.ToString() -Encoding UTF8
    $kb = [math]::Round((Get-Item $templatesOutput).Length / 1024, 1)
    Write-Host "  [templates]  vl-templates.md  ($($templateFiles.Count) files, ${kb} KB)"
    $generatedFiles.Add("vl-templates.md")
} else {
    Write-Warning "Templates directory not found: $templatesDir -- skipping vl-templates.md"
}

# --- Manifest ---
$manifestPath = Join-Path $KnowledgeDir "MANIFEST.md"
$timestamp    = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

function Get-SubmoduleShortHash([string] $dir) {
    try {
        $h = & git -C $dir rev-parse --short HEAD 2>$null
        return if ($h) { $h.Trim() } else { "unknown" }
    } catch { return "unknown" }
}

$gbCommit = Get-SubmoduleShortHash $GrayBookDir
$skCommit = Get-SubmoduleShortHash $SkillsDir

$manualFileDescriptions = @{
    "vl-quickref.md"              = "orientation / quick reference"
    "vl-patterns.md"              = "IOBox type XML examples + region wiring patterns + layout table (compact quick-reference not covered in one place by generated docs)"
    "vl-building-blocks.md"       = "VL type system: definitions, regions, pads, channels, reactive, delegates, C# interop, node call XML (authoritative ground truth for .vl structure)"
    "vl-common-graphs.md"         = "recurring subgraphs with pin-level notation, mined from 2053 help patches (Stride, Skia, Fuse, IO, Avalonia)"
    "vl-project-architecture.md"  = "multi-document project scaffolding, distilled from VL.Helga + vwgroup-medianight productions"
    "vvvv-internals-advanced.md"  = "bridge/HDE reflection, live registry, BridgeState (advanced/MCP-server internals)"
}

$manifestLines = @(
    "<!-- AUTO-GENERATED by scripts/build-knowledge.ps1 -- DO NOT EDIT MANUALLY -->"
    ""
    "# vvvv-mcp Knowledge Base Manifest"
    ""
    "Generated: $timestamp"
    ""
    "## Sources"
    ""
    "| Submodule | Path | Commit |"
    "|-----------|------|--------|"
    "| The-Gray-Book | knowledge/The-Gray-Book | $gbCommit |"
    "| tebjan-vvvv-skills | knowledge/tebjan-vvvv-skills | $skCommit |"
    "| Libraries.xml | vvvv/PublicContent on GitHub | fetched live |"
    ""
    "## Generated Files ($($generatedFiles.Count))"
    ""
    ($generatedFiles | ForEach-Object { "- ``$_``" })
    ""
    "## Manually Maintained (NOT overwritten by script)"
    ""
    ($ManualFiles | ForEach-Object {
        $desc = $manualFileDescriptions[$_]
        if ($desc) { "- ``$_`` -- $desc" } else { "- ``$_``" }
    })
    ""
    "## Auto-Generated by Other Scripts (not build-knowledge.ps1)"
    ""
    "- ``vl-help-examples.md`` — node->help-file index, generated by ``scripts/index-help-patches.ps1`` from LOCAL ``packs-community/`` (not redistributed). Used as ``help-example`` source in ``search_practical`` FTS index. NOT useful as a standalone knowledge doc."
    "- ``vl-forum-solutions.md`` — forum accepted solutions, generated by ``scripts/scrape-forum.ps1``"
    "- ``vl-forum-snippets.md`` — forum code snippets, generated by ``scripts/scrape-forum.ps1``"
    "- ``gray-book-image-text.md`` — gray book image descriptions, generated by ``scripts/describe-graybook-images.ps1``"
    ""
    "## Update Procedure"
    ""
    '```powershell'
    "# Pull latest from submodules + re-fetch Libraries.xml"
    "git submodule update --remote --merge"
    ""
    "# Regenerate all knowledge files"
    "./scripts/build-knowledge.ps1"
    ""
    "# Rebuild MCP server (only needed when C# code changed)"
    "dotnet build src/VvvvMcp.sln"
    '```'
    ""
    "## Image Note"
    ""
    "The Gray Book contains images (diagrams, screenshots) that cannot be embedded as text."
    "Generated files replace image refs with [IMAGE: filename -- alt-text] markers."
    "Future work: use vision models or extract SVG/alt-text to include diagram content."
)

Set-Content -LiteralPath $manifestPath -Value ($manifestLines -join "`n") -Encoding UTF8

# Summary
$totalKB = [math]::Round(
    (Get-ChildItem $KnowledgeDir -Filter "*.md" -File |
     Where-Object { $_.Name -ne "MANIFEST.md" } |
     Measure-Object -Property Length -Sum).Sum / 1024, 0)

Write-Host ""
Write-Host "Done: $($generatedFiles.Count) generated files, $totalKB KB total knowledge base"
Write-Host "Manifest written to: $manifestPath"
Write-Host ""
Write-Host "To update when submodules change:"
Write-Host "  git submodule update --remote --merge"
Write-Host "  ./scripts/build-knowledge.ps1"

