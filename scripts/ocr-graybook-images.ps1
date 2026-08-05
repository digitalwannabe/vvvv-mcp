<#
.SYNOPSIS
    OCRs the Gray Book images into a searchable knowledge markdown file.

.DESCRIPTION
    Repeatable workflow: builds the small WinRT OCR tool (scripts/OcrImages)
    and runs it over knowledge/The-Gray-Book/images/**, writing
    knowledge/gray-book-image-text.md.

    Run this whenever Gray Book images change:

        ./scripts/ocr-graybook-images.ps1

    Requires: Windows 10/11 with the English OCR language pack (present by
    default on en-US systems; otherwise install via Settings > Language).
#>
[CmdletBinding()]
param(
    [string]$ImagesRoot = (Join-Path $PSScriptRoot "..\knowledge\The-Gray-Book"),
    [string]$Output     = (Join-Path $PSScriptRoot "..\knowledge\gray-book-image-text.md")
)

$ErrorActionPreference = "Stop"
$toolProj = Join-Path $PSScriptRoot "OcrImages\OcrImages.csproj"

Write-Host "Building + running OCR over $ImagesRoot ..." -ForegroundColor Cyan
# (dotnet run rebuilds when sources changed — no stale binaries)
dotnet run --project $toolProj -- $ImagesRoot $Output
if ($LASTEXITCODE -ne 0) { throw "OCR run failed" }

Write-Host "Done -> $Output" -ForegroundColor Green
