<#
    OptiGames bootstrap.

        irm optigamesbeta.online/cli | iex

    Downloads the current OptiGames build into %LOCALAPPDATA%\OptiGamesTool and launches it
    elevated. Re-running is cheap: the download is skipped when the local copy already matches
    the published hash.
#>

$ErrorActionPreference = 'Stop'

# Assets are served straight off the newest GitHub release, so the site never has to
# host a binary and anyone can verify what they are running against the public build log.
$BaseUrl  = 'https://github.com/OptiGamesHQ/optigames-cli/releases/latest/download'
$RepoUrl  = 'https://github.com/OptiGamesHQ/optigames-cli'
$ToolDir  = Join-Path $env:LOCALAPPDATA 'OptiGamesTool'
$ExePath  = Join-Path $ToolDir 'OptiGames.exe'
$HashPath = Join-Path $ToolDir 'OptiGames.exe.sha256'

function Write-Step($Message) {
    Write-Host "  $Message" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  OPTIGAMES" -ForegroundColor Red -NoNewline
Write-Host "  -  Windows optimizer" -ForegroundColor DarkGray
Write-Host "  open source - $RepoUrl" -ForegroundColor DarkGray
Write-Host ""

# --- Preflight -------------------------------------------------------------

if ([Environment]::OSVersion.Version.Major -lt 10) {
    throw "OptiGames needs Windows 10 or 11."
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw "OptiGames needs 64-bit Windows."
}

New-Item -ItemType Directory -Force -Path $ToolDir | Out-Null

# TLS 1.2 is not the default on stock Windows 10 PowerShell 5.1.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# --- Work out whether we already have this build ---------------------------

$remoteHash = $null
try {
    $remoteHash = (Invoke-RestMethod -Uri "$BaseUrl/OptiGames.exe.sha256" -TimeoutSec 20).Trim()
} catch {
    Write-Step "Could not reach the update server; using the local copy if there is one."
}

$needsDownload = $true
if ((Test-Path $ExePath) -and $remoteHash) {
    $localHash = (Get-FileHash -Path $ExePath -Algorithm SHA256).Hash
    if ($localHash -eq $remoteHash) {
        $needsDownload = $false
        Write-Step "Already up to date."
    }
} elseif ((Test-Path $ExePath) -and -not $remoteHash) {
    $needsDownload = $false
}

# --- Download --------------------------------------------------------------

if ($needsDownload) {
    if (-not $remoteHash) { throw "Cannot download OptiGames - the update server is unreachable." }

    Write-Step "Downloading OptiGames..."
    $temp = "$ExePath.download"

    # Progress rendering makes Invoke-WebRequest an order of magnitude slower on PS 5.1.
    $priorProgress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri "$BaseUrl/OptiGames.exe" -OutFile $temp -UseBasicParsing
    } finally {
        $ProgressPreference = $priorProgress
    }

    $downloadedHash = (Get-FileHash -Path $temp -Algorithm SHA256).Hash
    if ($downloadedHash -ne $remoteHash) {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
        throw "Download failed its integrity check. Nothing was installed."
    }

    Move-Item -Path $temp -Destination $ExePath -Force
    Set-Content -Path $HashPath -Value $remoteHash -NoNewline

    # Clear the mark-of-the-web so SmartScreen does not block our own launch.
    Unblock-File -Path $ExePath -ErrorAction SilentlyContinue

    Write-Step "Verified."
}

# --- Launch elevated -------------------------------------------------------

Write-Step "Starting OptiGames (you will see a UAC prompt)..."
Write-Host ""

try {
    Start-Process -FilePath $ExePath -Verb RunAs | Out-Null
} catch {
    throw "OptiGames needs administrator rights to change system settings. Launch cancelled."
}
