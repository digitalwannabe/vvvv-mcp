<#
.SYNOPSIS
    Scrapes the vvvv forum (Discourse) for practical knowledge: solutions,
    code snippets, and answers from the core team and power users.

.DESCRIPTION
    Uses the Discourse JSON API to fetch topics and posts from the vvvv forum
    (https://forum.vvvv.org) and extracts:

      - Solved topics (accepted answers from the Discourse "Solved" plugin)
      - Posts containing code blocks (VL patches, C#, SDSL, XML)
      - Posts by known power users / vvvv developers
      - Topics tagged "gamma" or "vl"

    Output files:
      knowledge/vl-forum-solutions.md   – solutions and accepted answers
      knowledge/vl-forum-snippets.md    – code snippet extracts
      output/forum_raw.json             – full raw data for further processing

    Note: The Discourse API has a rate limit (~60 req/min unauthenticated).
    Fetching many topics takes time; use -MaxTopics to limit the run.

.PARAMETER ForumUrl
    vvvv Discourse instance URL. Default: https://forum.vvvv.org

.PARAMETER OutputDir
    Where to write .md files. Default: knowledge/ in repo root.

.PARAMETER RawOutputDir
    Where to write raw JSON. Default: VVVVNodeAnalyzer/output/

.PARAMETER MaxTopics
    Maximum topics to fetch per category. Default: 200.
    Set lower (e.g. 50) for a quick test run.

.PARAMETER Tags
    Discourse tags to search. Default: @('gamma', 'vl', 'vvvv-gamma')

.PARAMETER DevUsernames
    Forum usernames considered authoritative (core team + known experts).
    Posts from these users are prioritized and included verbatim.

.PARAMETER ApiKey
    Optional Discourse API key for higher rate limits. Leave empty to use
    anonymous access (60 req/min limit applies).

.EXAMPLE
    # Quick test: 50 topics
    ./scripts/scrape-forum.ps1 -MaxTopics 50

.EXAMPLE
    # Full run with API key for higher rate limit
    ./scripts/scrape-forum.ps1 -MaxTopics 500 -ApiKey "your-api-key"
#>
[CmdletBinding()]
param(
    [string]   $ForumUrl       = "https://forum.vvvv.org",
    [string]   $OutputDir      = "",
    [string]   $RawOutputDir   = "",
    [int]      $MaxTopics      = 200,
    [string[]] $Tags           = @('gamma', 'vl', 'vvvv-gamma', 'vvvv5'),
    [string[]] $DevUsernames   = @(
        'joreg', 'gregsn', 'tonfilm', 'dottore', 'velcrome', 'sebl',
        'ravazque', 'woei', 'robotanton', 'jens.a', 'm4d', 'evvvvil',
        'tebjan', 'bjoern', 'motzi', 'idwyr', 'kopffarben', 'readme'
    ),
    [string]   $ApiKey         = "",
    # Rebuild the .md files from the cached forum_raw.json (no network).
    [switch]   $FromRaw
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Needed for HtmlDecode (not auto-loaded in Windows PowerShell 5.1)
try { Add-Type -AssemblyName System.Web -ErrorAction Stop } catch { Write-Warning "System.Web unavailable: $_" }

# ── Resolve paths ─────────────────────────────────────────────────────────────

$scriptFile = $MyInvocation.MyCommand.Path
$repoRoot   = if ($scriptFile) { Split-Path (Split-Path $scriptFile -Parent) -Parent } else { Get-Location }

if (-not $OutputDir)    { $OutputDir    = Join-Path $repoRoot "knowledge" }
if (-not $RawOutputDir) { $RawOutputDir = Join-Path $repoRoot "VVVVNodeAnalyzer\output" }

New-Item -ItemType Directory -Force -Path $OutputDir    | Out-Null
New-Item -ItemType Directory -Force -Path $RawOutputDir | Out-Null

# ── HTTP helpers ──────────────────────────────────────────────────────────────

$headers = @{ 'Accept' = 'application/json' }
if ($ApiKey) {
    $headers['Api-Key']      = $ApiKey
    $headers['Api-Username'] = 'system'
    Write-Host "Using API key (higher rate limit)"
} else {
    Write-Host "Using anonymous API access (60 req/min limit applies -- use -MaxTopics to limit)"
}

$requestCount = 0
$rateLimitDelay = if ($ApiKey) { 50 } else { 1100 }  # ms between requests

function Invoke-ForumApi([string] $path) {
    $script:requestCount++
    $url = "$ForumUrl$path"
    try {
        $response = Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 30
        Start-Sleep -Milliseconds $rateLimitDelay
        return $response
    } catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode -eq 429) {
            Write-Warning "Rate limited -- waiting 60 seconds..."
            Start-Sleep -Seconds 60
            return Invoke-ForumApi $path   # retry once
        }
        Write-Warning "API error for $url : $_"
        return $null
    }
}

# ── Data containers + patterns (shared by fetch and FromRaw) ──────────────────

$solutions = [System.Collections.Generic.List[hashtable]]::new()
$snippets  = [System.Collections.Generic.List[hashtable]]::new()
$rawTopics = [System.Collections.Generic.List[hashtable]]::new()
$fetched = 0
$codeBlockPattern   = [regex]'```[^\n]*\n([\s\S]*?)```'
$sdslPattern        = [regex]'(?i)(shader\s+\w+\s*[:{]|TextureFX|FilterBase|MixerBase|ComputeShaderBase)'
$patchPattern       = [regex]'<Document|<NugetDependency|<Patch\s|<Node\s|NodeReference'
$csharpNodePattern  = [regex]'\[ProcessNode\]|public\s+class\s+\w+[\s\S]{0,200}Update\s*\('

if ($FromRaw) {
    # ── Rebuild solutions/snippets from the cached raw JSON (no network) ──────
    $rawPath0 = Join-Path $RawOutputDir "forum_raw.json"
    if (-not (Test-Path $rawPath0)) { throw "forum_raw.json not found at $rawPath0 — run without -FromRaw first." }
    Write-Host "Rebuilding from $rawPath0 (no network)..."
    $rawData = Get-Content -LiteralPath $rawPath0 -Raw | ConvertFrom-Json
    $fetched = $rawData.fetched
    foreach ($t in $rawData.topics) {
        foreach ($p in $t.posts) {
            $codes = @($p.codes)
            if ($p.isSolution -or $p.isDevPost) {
                $solutions.Add(@{
                    topicTitle = $t.title; url = "$($t.url)/$($p.postNum)"
                    username = $p.username; isSolution = [bool]$p.isSolution
                    isDevPost = [bool]$p.isDevPost
                    text = $p.text; codes = $codes
                })
            }
            foreach ($code in $codes) {
                $type = if ($sdslPattern.IsMatch($code)) { "sdsl" }
                        elseif ($patchPattern.IsMatch($code)) { "vl-patch" }
                        elseif ($csharpNodePattern.IsMatch($code)) { "csharp-node" }
                        elseif ($code -match '^\s*(public|private|class|namespace|using\s)') { "csharp" }
                        else { "other" }
                if ($type -ne "other" -or $p.isDevPost) {
                    $snippets.Add(@{
                        topicTitle = $t.title; url = "$($t.url)/$($p.postNum)"
                        username = $p.username; codeType = $type; code = $code
                    })
                }
            }
        }
    }
    Write-Host "Rebuilt: $($solutions.Count) solutions, $($snippets.Count) snippets from $fetched topics"
}
else {
# ── Collect topic IDs ─────────────────────────────────────────────────────────

$topicIds = [System.Collections.Generic.HashSet[int]]::new()

Write-Host ""
Write-Host "Fetching topics by tag..."

foreach ($tag in $Tags) {
    $page = 0
    do {
        $data = Invoke-ForumApi "/tag/$tag/l/latest.json?page=$page"
        if (-not $data -or -not $data.topic_list -or -not $data.topic_list.topics) { break }
        $topics = $data.topic_list.topics
        foreach ($t in $topics) { [void]$topicIds.Add($t.id) }
        Write-Host "  tag '$tag' page $page : $($topics.Count) topics (total: $($topicIds.Count))"
        $page++
        if ($topicIds.Count -ge $MaxTopics) { break }
    } while ($topics.Count -gt 0 -and $topicIds.Count -lt $MaxTopics)
    if ($topicIds.Count -ge $MaxTopics) { break }
}

Write-Host "Total unique topic IDs: $($topicIds.Count)"

# ── Fetch and parse topics ────────────────────────────────────────────────────

foreach ($id in ($topicIds | Select-Object -First $MaxTopics)) {
    $topic = Invoke-ForumApi "/t/$id.json?include_raw=true"
    if (-not $topic) { continue }

    $fetched++
    $title     = $topic.title
    $url       = "$ForumUrl/t/$($topic.slug)/$id"
    $tags      = @($topic.tags)
    $solvedProp = $topic.PSObject.Properties['accepted_answer']
    $solved    = if ($solvedProp) { $solvedProp.Value } else { $null }  # Discourse Solved plugin (absent when unsolved)

    $posts = @()
    if ($topic.post_stream -and $topic.post_stream.posts) { $posts = @($topic.post_stream.posts) }
    if ($posts.Count -eq 0) { continue }

    # Track extracted data for this topic
    $topicData = @{
        id      = $id
        title   = $title
        url     = $url
        tags    = $tags
        solved  = ($null -ne $solved)
        posts   = @()
    }

    foreach ($post in $posts) {
        $username  = $post.username
        # Prefer RAW markdown (code blocks are ``` fenced); cooked is HTML (code in <pre><code>)
        $rawProp    = $post.PSObject.Properties['raw']
        $cookedProp = $post.PSObject.Properties['cooked']
        $raw       = if ($rawProp -and $rawProp.Value) { $rawProp.Value } elseif ($cookedProp -and $cookedProp.Value) { $cookedProp.Value } else { "" }
        $postNum   = $post.post_number
        $isDevPost = $DevUsernames -contains $username
        $isSolution = ($solved -and $post.id -eq $solved.post_id)

        # Strip HTML tags for text analysis
        $text = [System.Text.RegularExpressions.Regex]::Replace($raw, '<[^>]+>', '')
        try { $text = [System.Web.HttpUtility]::HtmlDecode($text) } catch { }
        if ($null -eq $text) { $text = "" }

        # Extract code blocks
        $codeMatches = $codeBlockPattern.Matches($raw)
        $codes = @($codeMatches | ForEach-Object { $_.Groups[1].Value.Trim() })

        $postData = @{
            username   = $username
            postNum    = $postNum
            isSolution = $isSolution
            isDevPost  = $isDevPost
            text       = $(if ($text.Trim().Length -gt 0) { $text.Trim()[0..([Math]::Min(1000, $text.Trim().Length-1))] -join "" } else { "" })
            codes      = $codes
        }
        $topicData.posts += $postData

        # ── Solutions ────────────────────────────────────────────────────────

        if ($isSolution -or ($isDevPost -and $post.score -gt 5)) {
            $solutions.Add(@{
                topicTitle = $title
                url        = "$url/$postNum"
                username   = $username
                isSolution = $isSolution
                text       = $text.Trim()
                codes      = $codes
            })
        }

        # ── Code snippets ─────────────────────────────────────────────────────

        foreach ($code in $codes) {
            $type = if ($sdslPattern.IsMatch($code)) { "sdsl" }
                    elseif ($patchPattern.IsMatch($code)) { "vl-patch" }
                    elseif ($csharpNodePattern.IsMatch($code)) { "csharp-node" }
                    elseif ($code -match '^\s*(public|private|class|namespace|using\s)') { "csharp" }
                    else { "other" }

            if ($type -ne "other" -or $isDevPost) {
                $snippets.Add(@{
                    topicTitle = $title
                    url        = "$url/$postNum"
                    username   = $username
                    codeType   = $type
                    code       = $code
                })
            }
        }
    }

    $rawTopics.Add($topicData)

    if ($fetched % 10 -eq 0) {
        Write-Host "  Fetched $fetched / $($topicIds.Count) topics  (solutions: $($solutions.Count)  snippets: $($snippets.Count))"
    }
}

Write-Host ""
Write-Host "Done: $fetched topics, $($solutions.Count) solutions, $($snippets.Count) code snippets"

# ── Write raw JSON ────────────────────────────────────────────────────────────

$rawJson = @{
    generated  = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    forumUrl   = $ForumUrl
    tags       = $Tags
    fetched    = $fetched
    topics     = @($rawTopics)
} | ConvertTo-Json -Depth 15

$rawPath = Join-Path $RawOutputDir "forum_raw.json"
Set-Content -LiteralPath $rawPath -Value $rawJson -Encoding UTF8
Write-Host "  forum_raw.json  ($([math]::Round((Get-Item $rawPath).Length/1024,0)) KB)"
} # end else (network fetch)

# ── Write vl-forum-solutions.md ───────────────────────────────────────────────

$sbSolutions = New-Object System.Text.StringBuilder
[void]$sbSolutions.AppendLine("<!-- AUTO-GENERATED by scripts/scrape-forum.ps1 -- DO NOT EDIT MANUALLY -->")
[void]$sbSolutions.AppendLine("<!-- Re-run: ./scripts/scrape-forum.ps1 to refresh -->")
[void]$sbSolutions.AppendLine()
[void]$sbSolutions.AppendLine("# vvvv Forum Solutions")
[void]$sbSolutions.AppendLine()
[void]$sbSolutions.AppendLine("> Extracted accepted answers and high-quality dev responses from the vvvv gamma forum.")
[void]$sbSolutions.AppendLine("> Source: https://forum.vvvv.org -- tags: $($Tags -join ', ')")
[void]$sbSolutions.AppendLine()
[void]$sbSolutions.AppendLine("| Count | |")
[void]$sbSolutions.AppendLine("|---|---|")
[void]$sbSolutions.AppendLine("| Topics fetched | $fetched |")
[void]$sbSolutions.AppendLine("| Accepted solutions | $(@($solutions | Where-Object { $_.isSolution }).Count) |")
[void]$sbSolutions.AppendLine("| Dev responses | $(@($solutions | Where-Object { $_.isDevPost }).Count) |")
[void]$sbSolutions.AppendLine()

$grouped = $solutions | Group-Object { $_.topicTitle }
foreach ($grp in ($grouped | Sort-Object Name)) {
    [void]$sbSolutions.AppendLine("---")
    [void]$sbSolutions.AppendLine()
    [void]$sbSolutions.AppendLine("## $($grp.Name)")
    [void]$sbSolutions.AppendLine()

    foreach ($sol in $grp.Group) {
        $badge = if ($sol.isSolution) { " [SOLVED] **Accepted Solution**" } else { " (dev response)" }
        [void]$sbSolutions.AppendLine("**[$($sol.username)]($($sol.url))**$badge")
        [void]$sbSolutions.AppendLine()

        # Truncate very long text
        $text = $sol.text
        if ($text.Length -gt 1500) { $text = $text[0..1499] + "..." }
        [void]$sbSolutions.AppendLine($text)
        [void]$sbSolutions.AppendLine()

        foreach ($code in $sol.codes) {
            [void]$sbSolutions.AppendLine("``````")
            [void]$sbSolutions.AppendLine($code)
            [void]$sbSolutions.AppendLine("``````")
            [void]$sbSolutions.AppendLine()
        }
    }
}

$solPath = Join-Path $OutputDir "vl-forum-solutions.md"
Set-Content -LiteralPath $solPath -Value $sbSolutions.ToString() -Encoding UTF8
Write-Host "  vl-forum-solutions.md  ($([math]::Round((Get-Item $solPath).Length/1024,0)) KB)"

# ── Write vl-forum-snippets.md ────────────────────────────────────────────────

$sbSnippets = New-Object System.Text.StringBuilder
[void]$sbSnippets.AppendLine("<!-- AUTO-GENERATED by scripts/scrape-forum.ps1 -- DO NOT EDIT MANUALLY -->")
[void]$sbSnippets.AppendLine()
[void]$sbSnippets.AppendLine("# vvvv Forum Code Snippets")
[void]$sbSnippets.AppendLine()
[void]$sbSnippets.AppendLine("> Real-world vvvv code from the forum, organized by type.")
[void]$sbSnippets.AppendLine("> Source: https://forum.vvvv.org")
[void]$sbSnippets.AppendLine()

$typeOrder = @('sdsl','vl-patch','csharp-node','csharp')
foreach ($codeType in $typeOrder) {
    $typeSnippets = @($snippets | Where-Object { $_.codeType -eq $codeType })
    if ($typeSnippets.Count -eq 0) { continue }

    $typeName = switch ($codeType) {
        'sdsl'        { 'SDSL Shaders' }
        'vl-patch'    { 'VL Patch Fragments' }
        'csharp-node' { 'C# Node Classes' }
        'csharp'      { 'C# Code' }
        default       { $codeType }
    }
    $lang = switch ($codeType) {
        'sdsl'        { 'hlsl' }
        'vl-patch'    { 'xml' }
        default       { 'csharp' }
    }

    [void]$sbSnippets.AppendLine("## $typeName ($($typeSnippets.Count))")
    [void]$sbSnippets.AppendLine()

    # Deduplicate snippets by content hash
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($s in ($typeSnippets | Select-Object -First 50)) {
        $hash = [string]($s.code.GetHashCode())
        if (-not $seen.Add($hash)) { continue }

        [void]$sbSnippets.AppendLine("### [$($s.topicTitle)]($($s.url)) -- @$($s.username)")
        [void]$sbSnippets.AppendLine()
        [void]$sbSnippets.AppendLine("``````$lang")
        [void]$sbSnippets.AppendLine($s.code)
        [void]$sbSnippets.AppendLine("``````")
        [void]$sbSnippets.AppendLine()
    }
}

$snipPath = Join-Path $OutputDir "vl-forum-snippets.md"
Set-Content -LiteralPath $snipPath -Value $sbSnippets.ToString() -Encoding UTF8
Write-Host "  vl-forum-snippets.md  ($([math]::Round((Get-Item $snipPath).Length/1024,0)) KB)"
Write-Host ""
Write-Host "Forum scrape complete. Generated files are ready for the knowledge base."
Write-Host "To include them in search, run ./scripts/build-knowledge.ps1"
Write-Host "(These files are generated to knowledge/ directly, so they'll be picked up automatically.)"
