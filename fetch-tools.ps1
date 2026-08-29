#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads the third-party command-line tools Zn Tools needs at runtime
    (yt-dlp.exe, ffmpeg.exe, ffprobe.exe) and drops them next to the built app.

.DESCRIPTION
    These binaries are intentionally NOT committed to the repository — they are
    large and carry their own licenses (see README / .gitignore). At runtime the
    app resolves them via AppContext.BaseDirectory (see MainWindow.xaml.cs:524),
    i.e. the build output folder. This script fetches them straight into that
    folder so a fresh clone can just build & run.

    Only Windows PowerShell is required — no 7-Zip or other external tools.

.PARAMETER OutputDir
    Where to place the tools. Defaults to the Debug build output folder next to
    this script (bin\Debug\net8.0-windows). Point it at the Release folder when
    you build in Release.

.PARAMETER Force
    Re-download even if a tool is already present.

.EXAMPLE
    ./fetch-tools.ps1

.EXAMPLE
    ./fetch-tools.ps1 -OutputDir bin\Release\net8.0-windows -Force
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
# Skip the byte-by-byte progress UI (much faster downloads) and force TLS 1.2 so
# this also works on stock Windows PowerShell 5.1.
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$YtDlpUrl     = 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe'
# gyan.dev "essentials" build ships as a plain .zip (Expand-Archive can open it)
# and contains both ffmpeg.exe and ffprobe.exe.
$FfmpegZipUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

function Write-Step([string]$m) { Write-Host "  -> $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  OK $m"  -ForegroundColor Green }

# Make sure the target folder exists (it won't until the first build), then
# normalise it to an absolute path for clean logging.
$null = New-Item -ItemType Directory -Force -Path $OutputDir
$OutputDir = (Resolve-Path $OutputDir).Path
Write-Host ""
Write-Host "Zn Tools - fetching third-party binaries" -ForegroundColor Magenta
Write-Host "Target: $OutputDir"
Write-Host ""

# ---- yt-dlp.exe ----------------------------------------------------------
$ytDlpPath = Join-Path $OutputDir 'yt-dlp.exe'
if ((Test-Path $ytDlpPath) -and -not $Force) {
    Write-Ok "yt-dlp.exe already present (use -Force to refresh)."
} else {
    Write-Step "Downloading yt-dlp.exe ..."
    Invoke-WebRequest -Uri $YtDlpUrl -OutFile $ytDlpPath -UseBasicParsing
    Write-Ok "yt-dlp.exe"
}

# ---- ffmpeg.exe + ffprobe.exe -------------------------------------------
$ffmpegPath  = Join-Path $OutputDir 'ffmpeg.exe'
$ffprobePath = Join-Path $OutputDir 'ffprobe.exe'
if ((Test-Path $ffmpegPath) -and (Test-Path $ffprobePath) -and -not $Force) {
    Write-Ok "ffmpeg.exe / ffprobe.exe already present (use -Force to refresh)."
} else {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("znffmpeg_" + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Force -Path $tmp
    try {
        $zip = Join-Path $tmp 'ffmpeg.zip'
        Write-Step "Downloading FFmpeg (~80 MB, the big one) ..."
        Invoke-WebRequest -Uri $FfmpegZipUrl -OutFile $zip -UseBasicParsing
        Write-Step "Extracting ffmpeg.exe / ffprobe.exe ..."
        Expand-Archive -Path $zip -DestinationPath $tmp -Force
        $ff  = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe'  | Select-Object -First 1
        $ffp = Get-ChildItem -Path $tmp -Recurse -Filter 'ffprobe.exe' | Select-Object -First 1
        if (-not $ff -or -not $ffp) {
            throw "Could not find ffmpeg.exe/ffprobe.exe inside the downloaded archive."
        }
        Copy-Item $ff.FullName  $ffmpegPath  -Force
        Copy-Item $ffp.FullName $ffprobePath -Force
        Write-Ok "ffmpeg.exe / ffprobe.exe"
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- quick sanity check --------------------------------------------------
Write-Host ""
Write-Host "Installed versions:" -ForegroundColor Magenta
try {
    $ytv = (& $ytDlpPath --version) 2>$null
    Write-Host "  yt-dlp : $ytv"
    $ffv = ((& $ffmpegPath -version) 2>$null | Select-Object -First 1)
    Write-Host "  ffmpeg : $ffv"
} catch {
    Write-Host "  (version check skipped: $($_.Exception.Message))" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done. The tools are in place next to the app." -ForegroundColor Green
Write-Host "You can now run:  dotnet run --project ZnDownloader.csproj" -ForegroundColor Green
