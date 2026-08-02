<#
.SYNOPSIS
    Secret-Scan über das EchoPlay-Repo (Arbeitsbaum und Git-Historie).

.DESCRIPTION
    Wrappt die portable gitleaks.exe aus demselben Verzeichnis und nutzt die
    Repo-Konfiguration .gitleaks.toml. Ohne Schalter wird der Arbeitsbaum
    geprüft; -History nimmt die vollständige Git-Historie dazu.

    Exit-Code 0 = keine Befunde, 1 = Befunde, 2 = Werkzeug oder Konfiguration fehlt.

    Aufruf vom Repo-Root:
        pwsh tools/secret-scan.ps1
        pwsh tools/secret-scan.ps1 -History
        pwsh tools/secret-scan.ps1 -Json -OutFile bericht.json

    pwsh und nicht powershell: Die Datei ist UTF-8 ohne BOM. Windows PowerShell 5.1
    liest sie als ANSI und bricht an den Gedankenstrichen in den Ausgabezeilen ab.
#>

[CmdletBinding()]
param(
    [switch]$History,
    [switch]$Json,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

# Das Skript liegt in tools/, der Repo-Root ist genau eine Ebene darüber.
$repoRoot = Split-Path -Parent $PSScriptRoot
$gitleaks = Join-Path $PSScriptRoot 'gitleaks.exe'
$config   = Join-Path $repoRoot '.gitleaks.toml'

if (-not (Test-Path $gitleaks)) {
    Write-Error "gitleaks.exe nicht gefunden unter $gitleaks. Erst tools/install-gitleaks.ps1 ausführen."
    exit 2
}
if (-not (Test-Path $config)) {
    Write-Error ".gitleaks.toml nicht gefunden im Repo-Root ($config)."
    exit 2
}

$modus = if ($History) { 'git' } else { 'dir' }

# Nicht $args verwenden — das ist eine automatische Variable von PowerShell.
$gitleaksArgs = @($modus, $repoRoot, '--config', $config, '--no-banner')

if ($Json) {
    # Ohne Zielangabe in den Temp-Ordner schreiben, damit der Bericht nicht als
    # unversionierte Datei im Repo liegen bleibt.
    $bericht = if ($OutFile) { $OutFile } else { Join-Path $env:TEMP 'echoplay-secret-scan.json' }
    $gitleaksArgs += @('--report-format', 'json', '--report-path', $bericht)
    Write-Host "[secret-scan] Bericht: $bericht" -ForegroundColor Cyan
} else {
    $gitleaksArgs += '--verbose'
}

Write-Host "[secret-scan] gitleaks $modus-Scan startet..." -ForegroundColor Cyan
& $gitleaks @gitleaksArgs
$rc = $LASTEXITCODE

if ($rc -eq 0) {
    Write-Host "[secret-scan] Keine Befunde — sauber." -ForegroundColor Green
} elseif ($rc -eq 1) {
    Write-Host "[secret-scan] Befunde vorhanden — Ausgabe oben prüfen." -ForegroundColor Yellow
} else {
    Write-Host "[secret-scan] gitleaks-Fehler (Exit $rc)." -ForegroundColor Red
}

exit $rc
