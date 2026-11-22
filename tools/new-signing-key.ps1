<#
.SYNOPSIS
    Erzeugt das ECDSA-P-256-Schlüsselpaar, mit dem die EchoPlay-Setup.exe signiert wird.

.DESCRIPTION
    Der Auto-Updater installiert eine geladene Setup.exe nur, wenn ihre losgelöste Signatur
    (<Setup>.sig) unter dem in der App eingebetteten öffentlichen Schlüssel gültig ist
    (ReleaseSignatureVerifier). Dieses Skript erzeugt einmalig das Schlüsselpaar dazu:

      * Der PRIVATE Schlüssel (PKCS#8-PEM) landet unter -KeyPath. Er gehört gesichert in eine
        private Ablage außerhalb des Rechners und als Repository-Secret ECHOPLAY_SIGNING_KEY
        hinterlegt, niemals ins Repository.
      * Der ÖFFENTLICHE Schlüssel (SubjectPublicKeyInfo, Base64) wird ausgegeben und gehört in
        ReleaseSignatureVerifier.DefaultPublicKeySpkiBase64.

    Ein vorhandener Schlüssel wird nicht überschrieben: Installierte Fassungen vertrauen nur dem
    Schlüssel, mit dem sie gebaut wurden. Wer ihn ersetzt, schneidet sie vom Auto-Update ab, bis
    der Anwender eine neue Fassung von Hand installiert. Zum bewussten Wechsel -Force angeben.

.PARAMETER KeyPath
    Zielpfad des privaten Schlüssels (PEM), außerhalb jedes Repositorys.

.PARAMETER Force
    Überschreibt einen vorhandenen Schlüssel.

.EXAMPLE
    ./tools/new-signing-key.ps1 -KeyPath $env:USERPROFILE\EchoPlay-Signatur\private-key.pem
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KeyPath,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ((Test-Path -LiteralPath $KeyPath) -and -not $Force) {
    throw "Schlüssel existiert bereits: $KeyPath. Zum bewussten Wechsel -Force angeben."
}

# Ein Schlüssel im Arbeitsbaum wäre mit einem unbedachten 'git add' veröffentlicht.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$target = [System.IO.Path]::GetFullPath($KeyPath)
if ($target.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Der private Schlüssel darf nicht im Repository liegen: $target"
}

$dir = Split-Path -Parent $target
if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

# ECDSA P-256: kurze Signaturen, vollständig in .NET enthalten, keine fremde Bibliothek.
$ecdsa = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    Set-Content -LiteralPath $target -Value $ecdsa.ExportPkcs8PrivateKeyPem() -NoNewline
    $publicSpki = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
}
finally {
    $ecdsa.Dispose()
}

Write-Host 'Privater Schlüssel geschrieben (geheim halten, nicht committen):' -ForegroundColor Yellow
Write-Host "  $target"
Write-Host ''
Write-Host 'Öffentlicher Schlüssel für ReleaseSignatureVerifier.DefaultPublicKeySpkiBase64:' -ForegroundColor Cyan
Write-Host $publicSpki
