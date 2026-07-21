<#
.SYNOPSIS
    Baut die EchoPlay-Setup.exe: self-contained Publish + Inno-Setup-Kompilierung und
    legt Setup.exe samt App-Dateien im Verteilverzeichnis ab.

.DESCRIPTION
    1. Publiziert EchoPlay.App self-contained (.NET + Windows App SDK gebündelt, unpackaged)
       nach dist\setup-publish — eine lauffähige App ohne vorinstallierte Runtime, ohne MSIX,
       ohne Code-Signing-Zertifikat.
    2. Kompiliert EchoPlay.Setup\EchoPlay.Setup.iss mit Inno Setup 6 zur Setup.exe.
    3. Spiegelt den Publish nach <Distribution>\EchoPlay und kopiert die Setup.exe nach
       <Distribution>\EchoPlay-Setup-v<Version>.exe.
    4. Berechnet den SHA-256 der Setup.exe (für den GitHub-Release-Body — der Auto-Updater
       verifiziert die heruntergeladene Datei gegen diesen Hash).

    Ergebnis:
        <Distribution>\EchoPlay-Setup-v<Version>.exe
        <Distribution>\EchoPlay\...   (alle relevanten App-Dateien)

.PARAMETER Version
    Versionsnummer. Default: VersionPrefix aus Directory.Build.props.

.PARAMETER Distribution
    Verteilverzeichnis. Default: F:\Entwicklung\publish.

.PARAMETER Runtime
    Ziel-RID. Default: win-x64.
#>
param(
    [string]$Version,
    [string]$Distribution = 'F:\Entwicklung\publish',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\EchoPlay.App\EchoPlay.App.csproj'
$publishDir = Join-Path $root 'dist\setup-publish'
$distInternal = Join-Path $root 'dist'
$iss = Join-Path $root 'EchoPlay.Setup\EchoPlay.Setup.iss'

# Version aus Directory.Build.props (VersionPrefix = einzige Versionsquelle) ableiten,
# falls nicht explizit übergeben.
if (-not $Version) {
    $props = Join-Path $root 'Directory.Build.props'
    $m = Select-String -Path $props -Pattern '<VersionPrefix>([^<]+)</VersionPrefix>' | Select-Object -First 1
    if (-not $m) { throw 'VersionPrefix in Directory.Build.props nicht gefunden — Version explizit angeben.' }
    $Version = $m.Matches[0].Groups[1].Value.Trim()
}
Write-Host "Version: $Version" -ForegroundColor Cyan

# Inno-Setup-Compiler finden (Drive-agnostisch).
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) nicht gefunden. Installation: winget install JRSoftware.InnoSetup' }

Write-Host "Publish (self-contained .NET + Windows App SDK, unpackaged, $Runtime) ..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
& dotnet publish $appProject `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:Version=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)." }

# Verteil-Konvention absichern: keine Debug-Symbole in der Distribution. Der Release-Build
# setzt DebugType=none, hier nur der deterministische Guard gegen Drift (siehe INSTALLER.md).
$pdb = @(Get-ChildItem -Path $publishDir -Recurse -Filter '*.pdb' -File -ErrorAction SilentlyContinue)
if ($pdb.Count -gt 0) {
    throw "Publish enthält $($pdb.Count) *.pdb-Datei(en) — Debug-Symbole gehören nicht in die Distribution (DebugType in Release prüfen)."
}

Write-Host 'Setup.exe kompilieren (Inno Setup) ...' -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC fehlgeschlagen ($LASTEXITCODE)." }

$setupName = "EchoPlay-Setup-v$Version.exe"
$setupBuilt = Join-Path $distInternal $setupName
if (-not (Test-Path $setupBuilt)) { throw "Setup.exe nicht gefunden: $setupBuilt" }

# Verteilung: App-Dateien nach <Distribution>\EchoPlay spiegeln, Setup.exe daneben legen.
Write-Host "Verteile nach $Distribution ..." -ForegroundColor Cyan
$distApp = Join-Path $Distribution 'EchoPlay'
if (-not (Test-Path $Distribution)) { New-Item -ItemType Directory -Force $Distribution | Out-Null }
if (Test-Path $distApp) { Remove-Item -Recurse -Force $distApp }
New-Item -ItemType Directory -Force $distApp | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $distApp -Recurse -Force

$setupDist = Join-Path $Distribution $setupName
Copy-Item -Path $setupBuilt -Destination $setupDist -Force

# SHA-256 für den GitHub-Release-Body.
$hash = (Get-FileHash -LiteralPath $setupDist -Algorithm SHA256).Hash
$sizeMb = (Get-Item $setupDist).Length / 1MB

Write-Host ''
Write-Host ("Fertig: {0} ({1:N1} MB)" -f $setupDist, $sizeMb) -ForegroundColor Green
Write-Host ("App-Dateien: {0}" -f $distApp) -ForegroundColor Green
Write-Host ''
Write-Host 'Für den GitHub-Release-Body (der Auto-Updater verifiziert dagegen):' -ForegroundColor Yellow
Write-Host ("SHA256: {0}" -f $hash) -ForegroundColor White
