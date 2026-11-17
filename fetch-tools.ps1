#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads the third-party command-line tools Zn Tools needs at runtime
    (yt-dlp.exe, deno.exe, ffmpeg.exe, ffprobe.exe) and drops them next to the
    built app. An already present yt-dlp.exe is updated in place.

.DESCRIPTION
    These binaries are intentionally NOT committed to the repository — they are
    large and carry their own licenses (see README / .gitignore). At runtime the
    app resolves them via AppContext.BaseDirectory (see MainWindow.xaml.cs), i.e.
    the build output folder. This script fetches them straight into that folder
    so a fresh clone can just build & run.

    Every download comes over HTTPS from the tool's official release location and
    is checked against the SHA-256 checksum published alongside it, BEFORE it is
    extracted or put in place. A mismatch aborts the script and nothing is
    installed. "yt-dlp -U" (used when yt-dlp.exe is already present) performs the
    same SHA-256 check itself.

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
    [string]$OutputDir,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
# Resolved here rather than as the param default: Windows PowerShell 5.1 leaves
# $PSScriptRoot empty inside param() when the script is started with -File.
if (-not $OutputDir) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $OutputDir = Join-Path $scriptDir 'bin\Debug\net8.0-windows'
}
# Skip the byte-by-byte progress UI (much faster downloads) and force TLS 1.2 so
# this also works on stock Windows PowerShell 5.1.
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# gyan.dev "essentials" build ships as a plain .zip (Expand-Archive can open it)
# and contains both ffmpeg.exe and ffprobe.exe. Its checksum is <zip>.sha256.
$FfmpegZipUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

function Write-Step([string]$m) { Write-Host "  -> $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  OK $m"  -ForegroundColor Green }

# Tag of the latest GitHub release. Binary and checksum are then both fetched
# from that exact tag, so a release published mid-run can't mix them up.
function Get-LatestTag([string]$Repo) {
    $tag = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -UseBasicParsing).tag_name
    if (-not $tag) { throw "Could not determine the latest release of $Repo." }
    return $tag
}

# Reads a published checksum file and returns the SHA-256 in it (lower-case).
# With -FileName, only the line naming that file counts (multi-file SUMS lists).
function Get-PublishedSha256([string]$Url, [string]$FileName) {
    $raw = (Invoke-WebRequest -Uri $Url -UseBasicParsing).Content
    $text = if ($raw -is [byte[]]) { [Text.Encoding]::UTF8.GetString($raw) } else { [string]$raw }
    $lines = $text -split "`r?`n"
    if ($FileName) {
        $lines = $lines | Where-Object { $_ -match "^[0-9a-fA-F]{64}\s+\*?$([regex]::Escape($FileName))\s*$" }
    }
    foreach ($l in $lines) {
        if ($l -match '\b([0-9a-fA-F]{64})\b') { return $Matches[1].ToLowerInvariant() }
    }
    throw "No SHA-256 checksum found in $Url"
}

# Downloads $Url to $OutFile and verifies it against the published SHA-256.
# On mismatch the file is deleted and the script stops.
function Get-VerifiedFile([string]$Url, [string]$OutFile, [string]$ChecksumUrl, [string]$ChecksumFileName) {
    $expected = Get-PublishedSha256 -Url $ChecksumUrl -FileName $ChecksumFileName
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    $actual = (Get-FileHash -Algorithm SHA256 -Path $OutFile).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Remove-Item $OutFile -Force -ErrorAction SilentlyContinue
        throw "SHA-256 MISMATCH for $Url`n  expected: $expected`n  got:      $actual`nNothing was installed."
    }
    Write-Ok "SHA-256 verified: $(Split-Path $Url -Leaf)"
}

function New-TempDir([string]$Prefix) {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ($Prefix + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Force -Path $dir
    return $dir
}

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
    # An outdated yt-dlp quickly stops working with YouTube (HTTP 403), so
    # update the existing copy in place instead of just skipping it. yt-dlp's
    # own updater downloads from its GitHub releases and checks SHA2-256SUMS.
    Write-Step "yt-dlp.exe already present, updating it (yt-dlp -U) ..."
    & $ytDlpPath -U
    if ($LASTEXITCODE -ne 0) { throw "yt-dlp self-update failed (exit code $LASTEXITCODE)." }
    Write-Ok "yt-dlp.exe"
} else {
    $tag = Get-LatestTag 'yt-dlp/yt-dlp'
    $base = "https://github.com/yt-dlp/yt-dlp/releases/download/$tag"
    Write-Step "Downloading yt-dlp.exe $tag ..."
    $tmp = New-TempDir 'znytdlp_'
    try {
        $file = Join-Path $tmp 'yt-dlp.exe'
        Get-VerifiedFile -Url "$base/yt-dlp.exe" -OutFile $file -ChecksumUrl "$base/SHA2-256SUMS" -ChecksumFileName 'yt-dlp.exe'
        Copy-Item $file $ytDlpPath -Force
        Write-Ok "yt-dlp.exe"
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- deno.exe (JavaScript runtime for yt-dlp) ----------------------------
# yt-dlp needs an external JS runtime to solve YouTube's player challenges;
# without one, YouTube downloads fail with HTTP 403. deno is yt-dlp's default
# and the safest choice: yt-dlp runs it with no file system / network access.
$denoPath = Join-Path $OutputDir 'deno.exe'
if ((Test-Path $denoPath) -and -not $Force) {
    Write-Ok "deno.exe already present (use -Force to refresh)."
} else {
    $tag = Get-LatestTag 'denoland/deno'
    $zipName = 'deno-x86_64-pc-windows-msvc.zip'
    $base = "https://github.com/denoland/deno/releases/download/$tag"
    Write-Step "Downloading deno $tag (~40 MB) ..."
    $tmp = New-TempDir 'zndeno_'
    try {
        $zip = Join-Path $tmp $zipName
        Get-VerifiedFile -Url "$base/$zipName" -OutFile $zip -ChecksumUrl "$base/$zipName.sha256sum"
        Expand-Archive -Path $zip -DestinationPath $tmp -Force
        $d = Get-ChildItem -Path $tmp -Recurse -Filter 'deno.exe' | Select-Object -First 1
        if (-not $d) { throw "Could not find deno.exe inside the downloaded archive." }
        Copy-Item $d.FullName $denoPath -Force
        Write-Ok "deno.exe"
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- ffmpeg.exe + ffprobe.exe -------------------------------------------
$ffmpegPath  = Join-Path $OutputDir 'ffmpeg.exe'
$ffprobePath = Join-Path $OutputDir 'ffprobe.exe'
if ((Test-Path $ffmpegPath) -and (Test-Path $ffprobePath) -and -not $Force) {
    Write-Ok "ffmpeg.exe / ffprobe.exe already present (use -Force to refresh)."
} else {
    $tmp = New-TempDir 'znffmpeg_'
    try {
        $zip = Join-Path $tmp 'ffmpeg.zip'
        Write-Step "Downloading FFmpeg (~80 MB, the big one) ..."
        Get-VerifiedFile -Url $FfmpegZipUrl -OutFile $zip -ChecksumUrl "$FfmpegZipUrl.sha256"
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
    $dv = ((& $denoPath --version) 2>$null | Select-Object -First 1)
    Write-Host "  deno   : $dv"
    $ffv = ((& $ffmpegPath -version) 2>$null | Select-Object -First 1)
    Write-Host "  ffmpeg : $ffv"
} catch {
    Write-Host "  (version check skipped: $($_.Exception.Message))" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done. The tools are in place next to the app." -ForegroundColor Green
Write-Host "You can now run:  dotnet run --project ZnDownloader.csproj" -ForegroundColor Green
