<#
.SYNOPSIS
    Lädt gitleaks (portable Windows-x64 .exe) nach tools/.

.DESCRIPTION
    Prüfe Versionsangabe, lade ZIP und Prüfsummendatei von GitHub-Releases, vergleiche die
    SHA-256 und übernimm aus dem Archiv ausschließlich gitleaks.exe.
    Existierende tools/gitleaks.exe wird nur überschrieben, wenn die Version abweicht.

    Aufruf vom Repo-Root:
        pwsh tools/install-gitleaks.ps1
        pwsh tools/install-gitleaks.ps1 -Version 8.30.1

    pwsh und nicht powershell: Die Datei ist UTF-8 ohne BOM. Windows PowerShell 5.1
    liest sie als ANSI und bricht an den Gedankenstrichen in den Ausgabezeilen ab.
#>

[CmdletBinding()]
param(
    [string]$Version = '8.30.1'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $repoRoot 'tools'
$exePath  = Join-Path $toolsDir 'gitleaks.exe'

# Bei vorhandener Datei Version abgleichen
if (Test-Path $exePath) {
    $existing = & $exePath version 2>$null
    if ($existing -eq $Version) {
        Write-Host "[install-gitleaks] gitleaks v$Version bereits vorhanden — übersprungen." -ForegroundColor Green
        exit 0
    }
}

$releaseBase = "https://github.com/gitleaks/gitleaks/releases/download/v$Version"
$zipName = "gitleaks_${Version}_windows_x64.zip"
$workDir = Join-Path $env:TEMP "gitleaks_$Version"
$tmpZip = Join-Path $workDir $zipName
$tmpChecksums = Join-Path $workDir "gitleaks_${Version}_checksums.txt"

if (Test-Path $workDir) {
    Remove-Item $workDir -Recurse -Force
}
New-Item -ItemType Directory -Path $workDir | Out-Null

try {
    Write-Host "[install-gitleaks] Lade $releaseBase/$zipName ..." -ForegroundColor Cyan

    # Nur TLS 1.2 und 1.3: ältere Protokolle bietet GitHub nicht mehr an, und ein Fallback darauf
    # wäre eine Einladung zum Mitlesen.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    Invoke-WebRequest -Uri "$releaseBase/$zipName" -OutFile $tmpZip -UseBasicParsing
    Invoke-WebRequest -Uri "$releaseBase/gitleaks_${Version}_checksums.txt" -OutFile $tmpChecksums -UseBasicParsing

    # Ein ausführbares Programm aus dem Netz läuft erst nach dem Abgleich mit der Prüfsumme des
    # Releases — derselbe Grundsatz wie beim Updater der Anwendung.
    $expected = (Get-Content $tmpChecksums | Where-Object { $_ -match "\s$([regex]::Escape($zipName))$" } |
        ForEach-Object { ($_ -split '\s+')[0] } | Select-Object -First 1)
    if (-not $expected) {
        Write-Error "[install-gitleaks] FEHLER — keine Prüfsumme für $zipName im Release."
        exit 1
    }
    $actual = (Get-FileHash $tmpZip -Algorithm SHA256).Hash
    if ($actual -ne $expected.ToUpperInvariant()) {
        Write-Error "[install-gitleaks] FEHLER — Prüfsumme weicht ab (erwartet $expected, erhalten $actual)."
        exit 1
    }

    # Nur die ausführbare Datei übernehmen: Das Archiv bringt auch README.md und LICENSE mit,
    # die sonst die gleichnamigen Dateien in tools/ überschrieben.
    $extractDir = Join-Path $workDir 'extract'
    Expand-Archive -Path $tmpZip -DestinationPath $extractDir -Force
    Copy-Item (Join-Path $extractDir 'gitleaks.exe') $exePath -Force
}
finally {
    Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $exePath) {
    $installed = & $exePath version 2>$null
    Write-Host "[install-gitleaks] OK — gitleaks v$installed installiert nach $exePath" -ForegroundColor Green
} else {
    Write-Error "[install-gitleaks] FEHLER — gitleaks.exe nicht extrahiert."
    exit 1
}
