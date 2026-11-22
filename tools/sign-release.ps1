<#
.SYNOPSIS
    Signiert die EchoPlay-Setup.exe mit dem privaten Release-Schlüssel und legt die losgelöste
    Signatur als <Setup>.sig daneben.

.DESCRIPTION
    Der Auto-Updater lädt neben der Setup.exe deren Signatur und prüft sie vor dem Start des
    Installers gegen den eingebetteten öffentlichen Schlüssel (ReleaseSignatureVerifier). Fehlt
    die Signatur oder passt sie nicht, wird nicht installiert.

      * Verfahren: ECDSA P-256 über den SHA-256 der ganzen Datei, Format IEEE P1363 (64 Byte).
      * Ausgabe: die rohen Signaturbytes in <PackagePath>.sig (oder -OutPath).

    Die Signatur wird sofort gegengeprüft; schlägt das fehl, bricht das Skript ab. Der
    ausgegebene öffentliche Schlüssel muss ReleaseSignatureVerifier.DefaultPublicKeySpkiBase64
    entsprechen, sonst lehnen die installierten Fassungen das Update ab.

.PARAMETER PackagePath
    Die zu signierende Setup.exe.

.PARAMETER KeyPath
    Privater Schlüssel (PKCS#8-PEM), siehe new-signing-key.ps1.

.PARAMETER OutPath
    Zielpfad der Signatur. Vorgabe: <PackagePath>.sig

.EXAMPLE
    ./tools/sign-release.ps1 -PackagePath dist/EchoPlay-Setup-v1.4.0.exe `
                             -KeyPath $env:USERPROFILE\EchoPlay-Signatur\private-key.pem
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$KeyPath,
    [string]$OutPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) { throw "Setup nicht gefunden: $PackagePath" }
if (-not (Test-Path -LiteralPath $KeyPath))     { throw "Schlüssel nicht gefunden: $KeyPath" }
if (-not $OutPath) { $OutPath = "$PackagePath.sig" }

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportFromPem((Get-Content -LiteralPath $KeyPath -Raw))
    if ($ecdsa.KeySize -ne 256) {
        throw "Der Schlüssel ist kein P-256-Schlüssel ($($ecdsa.KeySize) Bit)."
    }

    # Dasselbe Verfahren wie die Prüfung in der App: SHA-256 über die ganze Datei.
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $PackagePath))
    $signature = $ecdsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)

    # Ohne bestandene Gegenprüfung wird nichts ausgeliefert.
    if (-not $ecdsa.VerifyData($bytes, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
        throw 'Gegenprüfung der erzeugten Signatur fehlgeschlagen.'
    }

    [System.IO.File]::WriteAllBytes($OutPath, $signature)
    $publicSpki = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
}
finally {
    $ecdsa.Dispose()
}

Write-Host "Signatur geschrieben: $OutPath" -ForegroundColor Green
Write-Host 'Öffentlicher Schlüssel (muss dem eingebetteten entsprechen):'
Write-Host $publicSpki
