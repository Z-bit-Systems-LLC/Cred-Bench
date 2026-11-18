#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Renders the app icon, src/UI/Windows/Logo.ico, and the About dialog logo, src/UI/Windows/Assets/Logo.png,
    from src/Assets/Logo.svg.
.DESCRIPTION
    Each size is rendered straight from the SVG by headless Microsoft Edge (or Chrome), so every size is
    drawn from the vectors and the text uses the installed Arial Rounded MT Bold font. Run it on Windows.
.EXAMPLE
    ./scripts/generate-icons.ps1
#>
param(
    [string]$Svg = (Join-Path $PSScriptRoot '..\src\Assets\Logo.svg'),
    [string]$IconPath = (Join-Path $PSScriptRoot '..\src\UI\Windows\Logo.ico'),
    [string]$PngPath = (Join-Path $PSScriptRoot '..\src\UI\Windows\Assets\Logo.png'),
    # Path to msedge.exe or chrome.exe; found automatically when left out
    [string]$Browser
)

$ErrorActionPreference = 'Stop'

$Svg = (Resolve-Path $Svg).Path
$IconPath = [System.IO.Path]::GetFullPath($IconPath)
$PngPath = [System.IO.Path]::GetFullPath($PngPath)

if (-not $Browser) {
    $Browser = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $Browser) { throw 'Neither Microsoft Edge nor Chrome was found. Pass -Browser.' }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "credbench-icons-$PID"
$profileDir = Join-Path $work 'profile'
New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
$svgUri = ([System.Uri]$Svg).AbsoluteUri

Add-Type -AssemblyName System.Drawing

# Renders the logo to fill a transparent Size x Size canvas
function Render-Logo([int]$Size, [string]$OutFile) {
    $html = Join-Path $work "render-$Size.html"
    Set-Content -Path $html -Encoding utf8 -Value @"
<!doctype html><html><head><style>
html,body{margin:0;padding:0;background:transparent;overflow:hidden}
body{width:${Size}px;height:${Size}px}
img{width:${Size}px;height:${Size}px;display:block}
</style></head><body><img src="$svgUri"></body></html>
"@
    if (Test-Path $OutFile) { Remove-Item $OutFile }
    $arguments = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--disable-extensions',
        '--disable-sync', "--user-data-dir=$profileDir", '--force-device-scale-factor=1',
        '--default-background-color=00000000', "--window-size=$Size,$Size",
        "--screenshot=$OutFile", ([System.Uri]$html).AbsoluteUri
    )
    Start-Process -FilePath $Browser -ArgumentList $arguments -Wait -WindowStyle Hidden
    if (-not (Test-Path $OutFile)) { throw "The browser did not write $OutFile" }

    $bitmap = [System.Drawing.Bitmap]::new($OutFile)
    try {
        if ($bitmap.Width -ne $Size -or $bitmap.Height -ne $Size) {
            throw "$OutFile is $($bitmap.Width)x$($bitmap.Height), expected ${Size}x${Size}"
        }
        if ($bitmap.GetPixel(0, 0).A -ne 0) { throw "$OutFile has no transparent background" }
    } finally {
        $bitmap.Dispose()
    }
}

# Writes an .ico whose entries are PNG images, which Windows has read since Vista
function Write-Icon([string[]]$PngFiles, [int[]]$Sizes, [string]$OutFile) {
    $images = $PngFiles | ForEach-Object { , [System.IO.File]::ReadAllBytes($_) }
    $stream = [System.IO.File]::Create($OutFile)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)              # reserved
        $writer.Write([uint16]1)              # type: icon
        $writer.Write([uint16]$images.Count)
        $offset = 6 + 16 * $images.Count
        for ($i = 0; $i -lt $images.Count; $i++) {
            $dimension = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
            $writer.Write([byte]$dimension)   # width
            $writer.Write([byte]$dimension)   # height
            $writer.Write([byte]0)            # palette size
            $writer.Write([byte]0)            # reserved
            $writer.Write([uint16]1)          # color planes
            $writer.Write([uint16]32)         # bits per pixel
            $writer.Write([uint32]$images[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($image in $images) { $writer.Write($image) }
    } finally {
        $writer.Dispose()
    }
}

try {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $pngs = foreach ($size in $sizes) {
        $png = Join-Path $work "icon-$size.png"
        Render-Logo $size $png
        $png
    }
    Write-Icon $pngs $sizes $IconPath
    Write-Host "Wrote $IconPath"

    # Large enough to stay sharp on the About dialog at 200% scaling
    New-Item -ItemType Directory -Force -Path (Split-Path $PngPath) | Out-Null
    Render-Logo 512 $PngPath
    Write-Host "Wrote $PngPath"
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
