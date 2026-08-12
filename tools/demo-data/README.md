# Demo-Bibliothek für Aufnahmen

Bilder der Anwendung für Webseite, Tutorials und Release-Notizen dürfen die private Sammlung
des Autors nicht zeigen — dort stehen gekaufte Serien mit Covern, die Verlagen gehören. Die
Skripte hier bauen stattdessen eine eigene Bibliothek auf: zwanzig gemeinfreie Titel, selbst
gesetzte Cover, Sprachproben aus dem Windows-Bestand als Tonspur.

Das Ergebnis sieht gefüllt aus (20 Serien, 84 Folgen, 168 Dateien), enthält aber nichts, was
jemandem gehört.

## Wichtig: die Datenbank liegt fest

`DatabasePathProvider` fragt `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`.
Das liest **nicht** die Umgebungsvariable `LOCALAPPDATA` — ein `$env:LOCALAPPDATA` vor dem
Start umzubiegen bringt also nichts (nachgemessen). Wer die echte Sammlung nicht anfassen
will, benennt den Ordner um und stellt ihn hinterher zurück:

```powershell
# Vorher — die Anwendung darf dabei nicht laufen
Rename-Item "$env:LOCALAPPDATA\EchoPlay" 'EchoPlay.beiseite'

# Nachher
Remove-Item "$env:LOCALAPPDATA\EchoPlay" -Recurse -Force
Rename-Item "$env:LOCALAPPDATA\EchoPlay.beiseite" 'EchoPlay'
```

Der Ordner ist rund 4,7 GB groß. Vor dem Umbenennen prüfen, dass kein `EchoPlay.App`-Prozess
mehr läuft — sonst hält er die Datenbank offen.

## Reihenfolge

Die Nummern im Dateinamen sind die Reihenfolge; jeder Schritt setzt den vorigen voraus.

`pwsh` und nicht `powershell`, aus demselben Grund wie bei den übrigen Skripten hier: Die
Dateien sind UTF-8 ohne BOM, Windows PowerShell 5.1 liest sie als ANSI. Aus „verlängert" wird
dann „verlÃ¤ngert", und im schlechteren Fall bricht eine Zeile mitten in einer Zeichenkette ab.

| Schritt | Skript | Was er tut |
|---|---|---|
| 1 | `1-build-covers.ps1` | 20 Cover als 600 × 600 JPG aus Farbfläche und Schrift |
| 2 | `2-build-library.csx` | Ordnerbaum, Tonspuren, ID3-Kennzeichnungen, Cover je Serie |
| 3 | `3-extend-tracks.ps1` | verlängert die 14-Sekunden-Proben auf drei bis sieben Minuten |
| 4 | `4-seed-database.csx` | Bibliothekspfad, Hörstand, Favoriten, Neuerscheinungen |
| 5 | `5-capture-window.ps1` | nimmt das Fenster auf, ohne schwarzen Rand |

```powershell
$temp = "$env:TEMP\echoplay-demo"
pwsh -File tools\demo-data\1-build-covers.ps1 -Target "$temp\cover"
dotnet script tools\demo-data\2-build-library.csx -- 'C:\Hörspiele-Demo' "$temp\cover"
pwsh -File tools\demo-data\3-extend-tracks.ps1 -Root 'C:\Hörspiele-Demo'
```

Dann die Anwendung starten, unter **Mediathek → Lokal** den Pfad `C:\Hörspiele-Demo`
einlesen lassen, beenden und erst danach:

```powershell
dotnet script tools\demo-data\4-seed-database.csx -- "$env:LOCALAPPDATA\EchoPlay\echoplay.db"
```

Die Reihenfolge ist nicht beliebig: Schritt 4 setzt Hörstände auf Folgen, die es erst nach
dem Einlesen gibt.

Drei Schritte fassen fremde Daten an, wenn der Pfad danebenzeigt — sie prüfen deshalb vorher
und brechen ab, statt zu schreiben:

| Schritt | Gefahr | Schranke |
|---|---|---|
| 2 | löscht den Zielordner | höchstens 400 vorhandene Dateien |
| 3 | überschreibt jede MP3 darunter | höchstens 400 MP3-Dateien |
| 4 | löscht Hörstände | höchstens 40 Serien und 300 Folgen |

Nachgemessen: Gegen `D:\Mp3` (93 908 Dateien) und gegen die private Datenbank (103 Serien,
5812 Folgen) steigen sie aus.

Schritt 4 räumt vorher auf (Hörstände, Neuerscheinungen, Auszeichnungen), damit ein zweiter
Lauf dasselbe Ergebnis liefert.

## Fallstricke, die Zeit gekostet haben

**`IsSubscribed` ist keine Überwachung.** Das Feld sagt, ob eine Serie zur Bibliothek gehört.
Wer es auf 0 setzt, macht alle Serien unsichtbar — die Überwachung steckt in `IsWatched`.

**Überwachte Serien holen echte Cover.** Steht eine Demo-Serie auf überwacht und trägt einen
Namen, den es wirklich gibt, fragt die Anwendung beim Anbieter nach und zeigt dessen Cover im
Dashboard. Genau das soll nicht ins Bild — deshalb setzt Schritt 4 die Neuerscheinungen
selbst und verlässt sich nicht auf eine Abfrage.

**MP3 ist ein Stromformat.** Die Windows-Sprachproben sind 14 Sekunden lang; die
Wiedergabeansicht zeigt dann „0:13 / -0:00" und sieht kaputt aus. Schritt 3 schreibt die
Bilddaten hinter dem ID3-Kopf mehrfach hintereinander — das Ergebnis ist länger und bleibt
abspielbar.

**Vollbild taugt nicht als Aufnahme.** Auf einem breiten Bildschirm ist die halbe Fläche leer,
und auf 800 Punkte verkleinert ist keine Beschriftung mehr lesbar. Schritt 5 setzt deshalb
eine feste Fenstergröße; 1280 × 1010 hat sich bewährt.

**Ein Fremdfenster im Bild fällt nur auf, wenn man hinsieht.** Schritt 5 prüft vor der
Aufnahme, dass EchoPlay wirklich im Vordergrund liegt, und bricht sonst ab.
