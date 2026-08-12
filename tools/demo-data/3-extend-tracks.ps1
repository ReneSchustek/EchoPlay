<#
.SYNOPSIS
    Verlängert die Tonspuren der Demo-Bibliothek auf glaubhafte Spielzeiten.

.DESCRIPTION
    Die Sprachproben aus dem Windows-Bestand sind 14 Sekunden lang. Die Wiedergabeansicht
    zeigt dann „0:13 / -0:00" — das sieht auf einer Projektseite nach einem Fehler aus, nicht
    nach einem Hörspiel.

    MP3 ist ein Stromformat: Werden die Bilddaten hinter dem ID3-Kopf mehrfach hintereinander
    geschrieben, entsteht eine längere Datei, die weiterhin abspielbar ist. Der Kopf mit
    Titel, Serie und Cover wird vorher gesichert und danach unverändert zurückgeschrieben.

    Die Spielzeiten liegen zwischen gut drei und knapp sieben Minuten und sind je Datei
    verschieden — eine Bibliothek, in der jede Folge exakt gleich lang ist, wirkt wie eine
    Attrappe.

.PARAMETER Root
    Wurzel der Demo-Bibliothek aus Schritt 2.
#>
param(
    [Parameter(Mandatory)][string]$Root
)

$ErrorActionPreference = 'Stop'

# Länge des ID3v2-Kopfs. Die vier Größenbytes sind „synchsafe": je sieben Bit gültig.
function Get-Id3HeaderLength([byte[]]$bytes) {
    if ($bytes.Length -le 10) { return 0 }
    if ($bytes[0] -ne 0x49 -or $bytes[1] -ne 0x44 -or $bytes[2] -ne 0x33) { return 0 }

    $size = (($bytes[6] -band 0x7F) -shl 21) -bor (($bytes[7] -band 0x7F) -shl 14) -bor
            (($bytes[8] -band 0x7F) -shl 7) -bor ($bytes[9] -band 0x7F)
    return 10 + $size
}

function Get-AudioPayload([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $start = Get-Id3HeaderLength $bytes
    $payload = New-Object byte[] ($bytes.Length - $start)
    [Array]::Copy($bytes, $start, $payload, 0, $payload.Length)
    return , $payload
}

$templates = @(
    (Get-AudioPayload 'C:\Windows\ImmersiveControlPanel\SystemSettings\Assets\Aria.mp3'),
    (Get-AudioPayload 'C:\Windows\ImmersiveControlPanel\SystemSettings\Assets\Guy.mp3'),
    (Get-AudioPayload 'C:\Windows\ImmersiveControlPanel\SystemSettings\Assets\Jenny.mp3')
)

# Schutz vor dem Fehlgriff: Dieses Skript überschreibt jede gefundene MP3-Datei. Zeigt der
# Pfad woandershin — auf einen Tippfehler, auf ein Laufwerk mit der echten Sammlung —, wäre
# der Bestand hinüber. Deshalb erst prüfen, dann schreiben.
if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    throw "Der Ordner existiert nicht: $Root"
}

$files = Get-ChildItem -LiteralPath $Root -Filter *.mp3 -Recurse -File

# Die Demo-Bibliothek hat 168 Dateien. Alles, was deutlich darüber liegt, ist etwas anderes.
$fileLimit = 400
if ($files.Count -gt $fileLimit) {
    throw "Abbruch: $($files.Count) MP3-Dateien unter $Root — das ist keine Demo-Bibliothek (erwartet werden höchstens $fileLimit)."
}

if ($files.Count -eq 0) {
    throw "Keine MP3-Dateien unter $Root — lief Schritt 2 mit demselben Zielordner?"
}

$index = 0

foreach ($file in $files) {
    $original = [System.IO.File]::ReadAllBytes($file.FullName)
    $headerLength = Get-Id3HeaderLength $original

    $header = New-Object byte[] $headerLength
    if ($headerLength -gt 0) { [Array]::Copy($original, 0, $header, 0, $headerLength) }

    $template = $templates[$index % $templates.Count]
    $repeats = 14 + ($index % 13)

    $stream = [System.IO.File]::Create($file.FullName)
    try {
        if ($headerLength -gt 0) { $stream.Write($header, 0, $header.Length) }
        for ($i = 0; $i -lt $repeats; $i++) { $stream.Write($template, 0, $template.Length) }
    }
    finally {
        $stream.Close()
    }

    $index++
}

$total = (Get-ChildItem $Root -Recurse -File | Measure-Object -Property Length -Sum).Sum
"{0} Tonspuren verlängert, Bibliothek jetzt {1:N0} MB" -f $index, ($total / 1MB)
