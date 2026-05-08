<#
.SYNOPSIS
    Laedt gitleaks (portable Windows-x64 .exe) nach tools/.

.DESCRIPTION
    Pruefe Versionsangabe, lade ZIP von GitHub-Releases, entpacke gitleaks.exe.
    Existierende tools/gitleaks.exe wird nur ueberschrieben, wenn die Version abweicht.

    Aufruf vom Repo-Root:
        powershell tools/install-gitleaks.ps1
        powershell tools/install-gitleaks.ps1 -Version 8.30.1
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
        Write-Host "[install-gitleaks] gitleaks v$Version bereits vorhanden — uebersprungen." -ForegroundColor Green
        exit 0
    }
}

$url = "https://github.com/gitleaks/gitleaks/releases/download/v$Version/gitleaks_${Version}_windows_x64.zip"
$tmpZip = Join-Path $env:TEMP "gitleaks_${Version}.zip"

Write-Host "[install-gitleaks] Lade $url ..." -ForegroundColor Cyan

# .NET-Default schlaegt teilweise auf Cert-Revocation-Check fehl; ein direkter
# WebRequest mit erlaubtem Skip-Revocation-Status umgeht das.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
Invoke-WebRequest -Uri $url -OutFile $tmpZip -UseBasicParsing

Expand-Archive -Path $tmpZip -DestinationPath $toolsDir -Force
Remove-Item $tmpZip -Force

if (Test-Path $exePath) {
    $installed = & $exePath version 2>$null
    Write-Host "[install-gitleaks] OK — gitleaks v$installed installiert nach $exePath" -ForegroundColor Green
} else {
    Write-Error "[install-gitleaks] FEHLER — gitleaks.exe nicht extrahiert."
    exit 1
}
