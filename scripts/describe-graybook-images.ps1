<#
.SYNOPSIS
    Describes Gray Book images with a LOCAL vision model via Ollama.

.DESCRIPTION
    Sends every image under knowledge/The-Gray-Book/images/** to a local (or remote)
    Ollama vision model and writes knowledge/gray-book-image-text.md.

    Built for slow GPUs / big runs:
    - INCREMENTAL: appends to the output file after every image (abort-safe)
    - RESUMABLE: re-running skips images already present in the output file
    - Tunable timeout and Ollama host (point at a stronger machine if you like)

    Usage:
        ./scripts/describe-graybook-images.ps1                      # full run, defaults
        ./scripts/describe-graybook-images.ps1 -MaxImages 5         # quick test
        ./scripts/describe-graybook-images.ps1 -Model qwen2.5vl:3b  # faster/weaker model
        ./scripts/describe-graybook-images.ps1 -OllamaUrl http://192.168.x.x:11434

    Requires: Ollama (https://ollama.com) with a vision model pulled
    (default qwen3-vl:8b — strong at UI/screenshot OCR; qwen2.5vl:3b is a fast fallback).
    Free VRAM afterwards with:  ollama stop <model>
#>
[CmdletBinding()]
param(
    [string]$ImagesRoot = (Join-Path $PSScriptRoot "..\knowledge\The-Gray-Book"),
    [string]$Output     = (Join-Path $PSScriptRoot "..\knowledge\gray-book-image-text.md"),
    [string]$Model      = "qwen3-vl:8b",
    [string]$OllamaUrl  = "http://localhost:11434",
    [int]$TimeoutSec    = 300,
    [int]$MaxImages     = 0
)

$ErrorActionPreference = "Stop"

# ── Ensure Ollama server is reachable ─────────────────────────────────────────
function Test-Ollama {
    try { Invoke-RestMethod "$OllamaUrl/api/tags" -TimeoutSec 5 | Out-Null; return $true }
    catch { return $false }
}

if (-not (Test-Ollama)) {
    if ($OllamaUrl -ne "http://localhost:11434") { throw "Ollama not reachable at $OllamaUrl" }
    Write-Host "Starting Ollama server..." -ForegroundColor Cyan
    Start-Process "ollama" -ArgumentList "serve" -WindowStyle Hidden
    $waited = 0
    while (-not (Test-Ollama) -and $waited -lt 30) { Start-Sleep 1; $waited++ }
    if (-not (Test-Ollama)) { throw "Ollama server did not start. Install from https://ollama.com" }
}

# ── Ensure the model is present ───────────────────────────────────────────────
$tags = Invoke-RestMethod "$OllamaUrl/api/tags"
$installed = @($tags.models | ForEach-Object { $_.name })
if (-not ($installed | Where-Object { $_ -like "$($Model.Split(':')[0])*" })) {
    Write-Host "Model '$Model' not installed. Pulling (one-time download)..." -ForegroundColor Yellow
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    & ollama pull $Model 2>$null
    $code = $LASTEXITCODE; $ErrorActionPreference = $prev
    if ($code -ne 0) { throw "Failed to pull $Model" }
}

# ── Priming prompt: tell the model exactly what it's looking at ───────────────
$prompt = @"
You are analyzing an image from the official documentation of vvvv gamma (a visual dataflow programming environment for .NET, file extension .vl).
Most of these images are SCREENSHOTS of the vvvv editor (the "HDE") on a dark theme. What you may see:
- node patches: rectangular nodes with a name and small pins, connected by links on a dark canvas
- menus: the Quad menu (top-left), the Documents menu, Application/Definitions patch explorers, the node browser
- UI panels: settings windows, inspectors, IOBoxes (value editors), help browser, tooltips
- occasional architecture diagrams, and a few photos of physical setups

Task:
1. TEXT: transcribe the readable text you see — menu entries, node names, pin labels, window/panel titles, button captions. Comma-separated. Skip unreadable glyphs instead of guessing.
2. SHOWS: one line identifying the kind of image (node patch, menu, settings panel, diagram, photo) and the specific feature/panel it depicts.
3. EXPLAINS: one line on what concept or workflow this image is meant to teach the reader.

Answer EXACTLY in this format, no preamble:
TEXT: <comma-separated readable text>
SHOWS: <one line>
EXPLAINS: <one line>
"@

# ── Resume support: collect images already described in the output file ───────
$done = New-Object System.Collections.Generic.HashSet[string]
if (Test-Path $Output) {
    foreach ($m in [regex]::Matches((Get-Content $Output -Raw), '(?m)^## `([^`]+)`')) {
        [void]$done.Add($m.Groups[1].Value)
    }
    if ($done.Count -gt 0) { Write-Host "Resuming — $($done.Count) images already described, skipping them." -ForegroundColor Yellow }
}

# ── Collect images ────────────────────────────────────────────────────────────
$images = Get-ChildItem $ImagesRoot -Recurse -File -Include *.png, *.jpg, *.jpeg | Sort-Object FullName
if ($MaxImages -gt 0) { $images = $images | Select-Object -First $MaxImages }
$todo = $images | Where-Object {
    $rel = $_.FullName.Substring($ImagesRoot.TrimEnd('\','/').Length).TrimStart('\','/') -replace '\\', '/'
    -not $done.Contains($rel)
}
Write-Host "Describing $($todo.Count) images with $Model (timeout ${TimeoutSec}s each)..." -ForegroundColor Cyan

# ── Incremental output: create the header if the file is new, then append ─────
if (-not (Test-Path $Output)) {
    $header = @"
# Gray Book - Image Descriptions (local vision model)

> AUTO-GENERATED by scripts/describe-graybook-images.ps1 - do not edit manually.
> Model: $Model via Ollama. Re-run when images change (already-described images are skipped).

---

"@
    Set-Content -LiteralPath $Output -Value $header -Encoding UTF8
}

$ok = 0; $i = 0; $failed = 0
foreach ($img in $todo) {
    $i++
    $rel = $img.FullName.Substring($ImagesRoot.TrimEnd('\','/').Length).TrimStart('\','/') -replace '\\', '/'
    try {
        $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($img.FullName))
        $body = @{ model = $Model; prompt = $prompt; images = @($b64); stream = $false } | ConvertTo-Json -Depth 5 -Compress

        # Raw UTF-8 byte body — a string body gets a BOM / wrong encoding from Invoke-RestMethod.
        $resp = Invoke-RestMethod "$OllamaUrl/api/generate" -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType "application/json" -TimeoutSec $TimeoutSec
        $text = ""; if ($resp -and $resp.response) { $text = $resp.response.Trim() }
        if ($text.Length -lt 3) { $failed++; Write-Warning "  empty response for $rel"; continue }

        # Append immediately so an abort never loses finished work
        $entry = "`n## ``$rel```n`n$text`n"
        Add-Content -LiteralPath $Output -Value $entry -Encoding UTF8
        $ok++
        if ($i % 5 -eq 0 -or $i -eq $todo.Count) { Write-Host "  $i / $($todo.Count) done ($($ok) ok, $failed failed)" }
    }
    catch {
        $failed++
        Write-Warning "  skip $rel : $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "Done: $ok described, $failed failed of $($todo.Count) -> $Output" -ForegroundColor Green
Write-Host "Free the model's VRAM with:  ollama stop $Model" -ForegroundColor DarkGray
