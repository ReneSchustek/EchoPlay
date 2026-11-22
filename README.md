# EchoPlay

**Hörspiel-Verwaltung für Windows:** Serien entdecken, Episoden verfolgen, lokal und online.

EchoPlay ist eine Desktop-Anwendung für alle, die ihre Hörspielsammlung in Ordnung halten wollen. Serien lassen sich aus Online-Quellen (Spotify, Apple Music) importieren oder aus lokalen Audiodateien einlesen. Die App merkt sich, wie weit jede Episode gehört ist, zeigt Neuerscheinungen an und hilft dabei, fehlende Folgen zu finden.

---

## Funktionsumfang

### Bibliothek

- **Online-Mediathek.** Serien aus Spotify und Apple Music durchsuchen, importieren und verwalten. Die Kachelansicht zeigt Cover, Favoriten-Stern und Überwachungs-Icon.
- **Lokale Mediathek.** Audiodateien werden gescannt und automatisch Serien und Episoden zugeordnet. Navigiert wird in drei Spalten (Serien | Folgen | Tracks).
- **Folgennummern aus dem Ordnernamen.** Die Nummer einer Folge kommt aus dem Ordnernamen, auch wenn er nicht genau dem eingestellten Muster folgt („Serie - 082 - Titel"). Ordner ohne Nummer wie Adventskalender oder Kinofilme bleiben sichtbar, belegen aber keine Nummer einer regulären Folge. Nur eine Serie, deren Ordner gar keine Nummern tragen, wird in Ordnerreihenfolge durchgezählt. Beim erneuten Einlesen werden falsch gespeicherte Nummern berichtigt.
- **Folgen auf mehreren CDs.** Liegen die Dateien einer Folge in Unterordnern wie „CD 1“, „CD 2“ oder „100 A“, „100 B“, gehören sie zur Folge und spielen in Ordnerreihenfolge ab. Ein Unterordner mit eigener Folgennummer bleibt eine eigene Folge.
- **Neue Folgen beim erneuten Einlesen.** Kommt zu einer bekannten Serie ein Ordner dazu, erscheint er nach dem nächsten Einlesen in der Mediathek. Unveränderte Folgen werden dabei übersprungen, statt alle Tags neu zu lesen; das zweite Einlesen geht entsprechend schneller.
- **Filter „Unvollständig“.** Zeigt die Serien mit einer Lücke in der Nummernfolge oder mit Folgen, die der Anbieter nach der letzten vorhandenen schon geliefert hat, wie der Bericht „Fehlende Folgen“. Bei Serien, deren Dateien flach im Serienordner liegen, erkennt nur der Filter Lücken, weil der Bericht die Nummern aus den Ordnernamen liest.
- **Nur-Online-Modus.** Wer keine lokale Sammlung hat, blendet die lokale Mediathek einfach komplett aus.
- **Buchstaben-Abschnitte mit Sprungleiste.** Beide Mediatheken gliedern ihre Serien nach Anfangsbuchstaben. Die Leiste darüber filtert nicht, sie springt zum gewählten Buchstaben, und der übrige Bestand bleibt sichtbar. Buchstaben ohne Einträge sind erkennbar, lassen sich aber nicht anwählen. In der Online-Mediathek erscheint die Gliederung nur bei Sortierung nach Namen, denn nach Folgenanzahl sortiert gäbe es keine Buchstabenordnung, auf die eine Sprungmarke zeigen könnte.
- **Folgenbereich zuklappen ohne Zurückscrollen.** Ist eine Serie aufgeklappt, steht unten rechts ein Knopf, der beim Scrollen mitwandert. Er schließt den Bereich und holt die Serie zurück ins Blickfeld, statt einen irgendwo in der Liste stehen zu lassen.

### Dashboard

- **Neuerscheinungen.** Neue Folgen überwachter Serien, nach Monaten gruppiert. Die Daten kommen live aus der iTunes-API.
- **Veröffentlichungen.** Was in einem Monat bei allen vorhandenen Serien erscheint oder angekündigt ist, mit Umschalter vom Vormonat bis zur spätesten Ankündigung. Jede Kachel zeigt, ob die Folge schon lokal oder in der Mediathek liegt. Die Liste baut sich nach dem Start im Hintergrund aus iTunes auf und gilt einen Tag; Serien ohne gespeicherte iTunes-Kennung zählen nur bei genau gleichem Künstlernamen.
- **Angekündigt.** Eigener Menüeintrag: alle angekündigten Folgen der vorhandenen Serien, nach Monat und Erscheinungstag, aus demselben Kalender. Ein Klick öffnet die Serie. Die Neuerscheinungen der Startseite zeigen nur, was schon erschienen ist.
- **Favoriten.** Schnellzugriff auf Lieblingsserien, per Drag & Drop sortierbar. Wer eine Serie favorisiert, schaltet damit auch die Überwachung auf Neuerscheinungen ein; einzeln abschalten lässt sie sich weiterhin über das Auge-Symbol. Überwachte Titel bleiben gemerkt: Nach einem Leeren der Mediathek bekommen gleichnamige Serien ihre Überwachung automatisch zurück.
- **Weiterhören.** Serien mit angefangenen, aber nicht abgeschlossenen Episoden.
- **Zuletzt gehört.** Die letzten Wiedergaben auf einen Blick.

### Wiedergabe

- Integrierter Audioplayer mit MiniPlayer, Zeitanzeige und Playlist-Unterstützung.
- 8 Audioformate: MP3, M4A, FLAC, OGG, WMA, WAV, AAC, Opus.
- Die Position wird automatisch gespeichert. Beim nächsten Start geht es an der letzten Stelle weiter, auch bei Folgen, die aus mehreren Dateien bestehen.
- **Fortschritt der ganzen Folge.** Neben der Position in der laufenden Datei zeigen beide Player, wie weit das Hörspiel insgesamt ist.
- **Lautstärke.** Regler und Stummschaltung gibt es in beiden Playern; die Einstellung übersteht einen Neustart.
- **Wiederhören.** Eine bereits gehörte Folge bleibt als gehört markiert und taucht zusätzlich unter „Angefangen" auf, solange darin eine offene Stelle steht.
- **Album beim Anbieter öffnen.** Folgen ohne lokale Datei lassen sich über das Kontextmenü in Spotify oder Apple Music aufrufen. Ist die Spotify-App installiert, öffnet sich diese statt des Browsers, und dort ist der Nutzer bereits angemeldet. Die Wiedergabe selbst steuert er dann beim Anbieter; eine Fortsetzung wie bei lokalen Dateien gibt es dort nicht.

### Fehlende Folgen

- Die Lückensuche vergleicht die vorhandenen Ordner mit dem Bestand beim Anbieter und meldet, welche Nummern fehlen. Sie beginnt bei der kleinsten Folge, die tatsächlich vorhanden ist. So gelten Serien, deren Sammlung erst später einsetzt, nicht als komplett unvollständig.
- Der Bericht über alle Serien nennt nur Serien, denen etwas fehlt. Dazu gehören auch die Folgen nach der letzten vorhandenen, die der Anbieter schon geliefert hat. Die kennt die App aus der Online-Mediathek, auch ohne Netz; „Online + Offline" fragt zusätzlich den aktuellen Stand ab.

### Tag-Manager

- Audio-Metadaten lesen und schreiben (TagLib#).
- Online-Lookup über MusicBrainz, der die Angaben automatisch aus der Ordnerstruktur erkennt.
- Batch-Tagging: gemeinsame Tags auf alle Dateien eines Ordners anwenden.
- Rekursive Ordnersuche und Mehrfachauswahl.
- Dateien nach einem konfigurierbaren Muster umbenennen.
- **Ordnerstruktur aufbauen.** Liegen die Dateien einer Serie flach im Serienordner, bekommt jede Folge einen eigenen Ordner nach dem eingestellten Muster. Mediathek, Spuren und Hörstände zeigen danach auf die neuen Pfade; ein abgebrochener Umbau wird beim nächsten Öffnen der Vorschau zurückgenommen.
- **Folgenordner nach Tags umbenennen.** Über das Kontextmenü einer Serie in der lokalen Mediathek bekommen die Folgenordner Namen nach dem eingestellten Ordnermuster, etwa „087 - Geheimnisvoller Liebestrank". Die Nummer kommt aus dem Ordnernamen, der Titel aus dem Album-Tag; fehlt der Tag, aus dem Ordnernamen. Eine Vorschau zeigt jede Änderung vorher und nennt, was bleibt und warum: Sonderfolgen ohne Nummer, schon vergebene Ziele, zu lange Pfade. Mediathek, Spuren und Hörstände bleiben zugeordnet. Bricht ein Lauf ab, nimmt die nächste Vorschau ihn zurück.
- **Daten bearbeiten.** Über das Kontextmenü lassen sich der Titel einer Serie sowie Titel, Nummer, Interpret und Erscheinungsjahr einer Folge berichtigen. Die Folge wird in der Mediathek, in den Tags ihrer Dateien und im Ordnernamen geändert, sodass die Änderung auch nach dem nächsten Einlesen bleibt.

### Weitere Features

- **Suche und Filter auf jeder Listenseite.** Mediatheken, Serien-Detail, Suche, Protokoll und Tag-Manager sind gleich aufgebaut: Seitenkopf, Suchfeld, Filter als Chips. Gesucht wird schon beim Tippen. Findet die Suche nichts, sagt die Seite das anders als bei leerem Bestand und bietet den Weg zurück zum vollständigen Bestand an. Such- und Filtereinstellungen überstehen den Wechsel auf eine andere Seite und zurück.
- **Anbieter-Suche mit laufender Anzeige.** Die Treffer erscheinen einzeln, sobald sie feststehen, und nicht erst nach dem vollständigen Suchlauf; jede Kachel bringt ihr Cover gleich mit. Künstler ohne Bezug zu Namen oder Genre scheiden vor der aufwendigen Prüfung aus, bekannte Serien werden gar nicht erst geprüft. Wer während einer laufenden Suche weitertippt, bricht sie ab, ohne dass daraus Fehlermeldungen entstehen.
- **Cover-System.** 5 Online-Anbieter, lokaler Fallback, DB-Cache. Die Bilder kommen von einem Auslieferungsnetz und laden deshalb nebeneinander statt hintereinander; die Cover einer Trefferseite stehen zusammen.
- **Feste Zeitgrenzen für Anfragen ins Netz.** Eine Gegenstelle, die nicht antwortet, hält die App nicht auf: Anfragen an Spotify, iTunes und die Cover-Anbieter geben nach spätestens 30 Sekunden auf, Wiederholungen eingerechnet, die Anmeldung bei Spotify nach 10 Sekunden. Die Suche nach einer neuen Version hält den Start höchstens 5 Sekunden auf.
- **6 Themes.** Ruhrcoder, ModernClassic, PaperCoffee, MidnightLibrary, ForestSignal, AmberWhiskey. Aufbau, Kachelform und Abstände sind in allen Themes gleich, nur die Farbe bestimmt das gewählte Theme. Text hat in jedem Theme einen Kontrast von mindestens 4,5:1.
- **Lokalisierung.** Deutsch und Englisch, zur Laufzeit umschaltbar.
- **Nur eine laufende Anwendung.** Ein zweiter Start öffnet kein zweites Fenster, sondern holt das vorhandene nach vorn, auch aus der Taskleiste. Der Neustart für den Sprachwechsel ist davon nicht betroffen.
- **Auto-Update.** Beim Start sucht die App über GitHub Releases nach neuen Versionen. Heruntergeladen wird nur über HTTPS von einem GitHub-Release-Host. Installiert wird nur, wenn die Setup-Datei die Signatur des Herausgebers trägt und zum SHA-256-Hash aus dem Release-Text passt; fehlt eines davon oder stimmt es nicht, wird nicht installiert. So nützt es nichts, Setup-Datei und Hash eines Release auszutauschen: Eine gültige Signatur entsteht nur mit dem privaten Schlüssel, und den geben weder die Release-Seite noch die Schnittstelle von GitHub heraus.
- **Umzug auf einen anderen Rechner.** Unter Einstellungen → Wartung schreibt „Daten exportieren“ Wiedergabestand, Gehört-Markierungen, Favoriten, Überwachung und Einstellungen in eine Datei an einem frei gewählten Ort; Zugangsdaten gehen nur mit, wenn man sie mit einem Kennwort verschlüsselt. „Daten einspielen“ prüft die Datei, zeigt vorher, was ersetzt wird, sichert den bisherigen Bestand und spielt nach einem Neustart ein. Stammpfad, Lautstärke und Zugangsdaten des Zielrechners bleiben dabei erhalten, die Pfade der Mediathek werden auf seinen Stammpfad umgerechnet.
- **Abgleich zwischen Geräten.** Mehrere Rechner halten denselben Stand über einen gemeinsamen Ordner, etwa im Cloud-Speicher: Hörstand, Gehört-Markierungen, Favoriten, Überwachung, gemeinsame Einstellungen und die Reihenfolge der Startseite. Jedes Gerät schreibt nur in seinen eigenen Unterordner und nur neue Dateien, deshalb gibt es keine Konfliktkopien. Gleichzeitige Änderungen werden nach festen Regeln zusammengeführt: Die jüngste Änderung gewinnt, die Position kommt von der jüngsten Wiedergabe, und was einmal gehört ist, bleibt gehört. Pfade, Lautstärke und Zugangsdaten bleiben auf ihrem Gerät. Eingerichtet wird er unter Einstellungen → Verwaltung: Ordner wählen, einschalten, und auf einem neuen Rechner vorher die Daten einspielen. Dort stehen auch der letzte Abgleich und die bekannten Geräte, die sich entfernen lassen; die Statusleiste zeigt, wenn er läuft oder gestört ist. Das Format ist in [`ABGLEICH.md`](ABGLEICH.md) beschrieben.
- **Mediathek mitnehmen.** „Mediathek packen“ schreibt die ganze lokale Mediathek samt Ordnern und dem Datenpaket in ein Archiv (`*.echoplay-mediathek`), ohne die schon komprimierten Audiodateien noch einmal zu packen, mit Prüfsumme je Datei. „Mediathek auspacken“ legt sie auf dem Zielrechner in einem leeren Ordner ab, prüft jede Datei und spielt das Datenpaket ein; der Ordner wird zum Stammpfad, ein neuer Scan ist nicht nötig. Ein Abbruch hinterlässt weder ein halbes Archiv noch halb ausgepackte Dateien.
- **Statistik.** Sammlungsübersicht, Hörfortschritt, Kennzahlen.
- **Kontexthilfe.** TeachingTips auf jeder Seite für neue Nutzer.
- **Hinweise dauerhaft ausblenden.** Jeder Hinweis und jede Rückfrage hat ein Häkchen „nicht wieder anzeigen". Bei Rückfragen merkt sich die App nur eine Zustimmung; wer abbricht, wird beim nächsten Mal wieder gefragt. Unerwartete Fehler und ein fehlgeschlagener Start sind davon ausgenommen, die müssen sichtbar bleiben. Unter Einstellungen → Verwaltung stehen alle ausgeblendeten Hinweise mit Datum und lassen sich einzeln oder gesammelt zurückholen.

---

## Installation

Die fertige Anwendung gibt es als Windows-Installer unter
[GitHub Releases](https://github.com/ReneSchustek/EchoPlay/releases). Lade die
`EchoPlay-Setup-vX.Y.Z.exe` der neuesten Version herunter und führe sie aus. EchoPlay
installiert sich nach `C:\Program Files\EchoPlay`; dafür fragt Windows einmal nach
Adminrechten. Eine vorinstallierte Runtime braucht es nicht, weil .NET und das Windows App
SDK im Paket enthalten sind. Eine ältere Installation im Benutzerprofil entfernt das Setup
dabei selbst; Einstellungen und Mediathek unter `%LOCALAPPDATA%\EchoPlay` bleiben erhalten.

Beim Start sieht die App selbst nach neuen Versionen und prüft heruntergeladene
Updates gegen ihre Signatur und den SHA-256-Hash, der im Release hinterlegt ist. Wie der Installer gebaut
wird, beschreibt [tools/publish-setup.ps1](tools/publish-setup.ps1) samt der Inno-Setup-Vorlage in
[EchoPlay.Setup](EchoPlay.Setup/EchoPlay.Setup.iss); was sich je Version geändert hat, steht in
den Release-Notizen unter [GitHub Releases](https://github.com/ReneSchustek/EchoPlay/releases).

Lädst du das Setup **von Hand** herunter, lohnt sich der Hash-Vergleich. SmartScreen warnt
trotzdem, weil das Setup kein Windows-Codesignatur-Zertifikat trägt; die Signatur daneben
(`.sig`) prüft nur der eingebaute Updater. Den erwarteten Wert findest du im Text des
Releases:

```powershell
Get-FileHash .\EchoPlay-Setup-vX.Y.Z.exe -Algorithm SHA256
```

---

## Sicherheit

Ohne Server-Backend und ohne Nutzerkonten bleibt die Angriffsfläche klein. Die Stellen,
an denen fremde Daten ins Programm gelangen, sind trotzdem abgesichert:

| Bereich | Maßnahme |
|---|---|
| Auto-Update | Signaturpflicht (ECDSA P-256 über SHA-256, öffentlicher Schlüssel eingebettet, privater Schlüssel nicht im Repository), dazu SHA-256-Pflichtprüfung mit zeitkonstantem Vergleich; die Setup-Datei bleibt von der Prüfung bis zum Start des Installers gegen Schreiben gesperrt. HTTPS-Zwang, Host-Bindung an GitHub-Release-Hosts, Versionsformat als Whitelist |
| Spotify-Zugangsdaten | Windows DPAPI im `CurrentUser`-Scope mit anwendungsspezifischer Entropie; nie im Code, in `appsettings.json` oder in User Secrets |
| Dateizugriffe | Jede schreibende Stelle prüft über `SecurePathHelper.IsPathInside`, dass der Zielpfad im erlaubten Verzeichnis bleibt, auch nach Auflösung von `..` und Verknüpfungen |
| Links öffnen | Nur `http`/`https` oder ein exakt erwartetes Anwendungsschema; eine Adresse aus der Datenbank startet damit kein beliebiges Programm |
| Fremde Adressen | Adressen aus Anbieter-Antworten, Release-Feed und Datenbank werden an der Grenze einmal geprüft (absolut, nur `http`/`https`) und danach als `Uri` geführt; eine unlesbare Adresse führt zum Platzhalter statt zu einem Absturz |
| Datenbankzugriff | EF Core mit LINQ; die wenigen Roh-SQL-Stellen nutzen ausschließlich feste Fragmente und Parameter |
| Datenpakete | Ein eingespieltes Paket gilt als fremde Datei: nur feste Eintragsnamen (kein Zip-Slip), Grenzen gegen Archivbomben, SHA-256 je Eintrag, Ablehnung neuerer Fassungen, Sicherung vor dem Ersetzen. Zugangsdaten darin nur mit Kennwort (AES-GCM, PBKDF2 mit 600.000 Durchläufen). Ein Mediathek-Archiv wird genauso behandelt: jeder Pfad geprüft (kein `..`, kein Laufwerk, keine Gerätenamen), jeder Eintrag mit dem Manifest abgeglichen, ausgepackt nur in einen leeren Ordner |
| Protokolle | Verzeichnisse werden durch einen Kurz-Hash ersetzt, Geheimnisse in Abfrageparametern und Zugangsdaten in Adressen durch `***` |
| Abhängigkeiten | `dotnet list package --vulnerable` als hartes Gate, abgekündigte Pakete als Hinweis; CodeQL und die Prüfung der Historie in der CI |
| Geheimnisse im Quellbaum | `gitleaks` läuft vor jedem Abschluss über den Arbeitsbaum und bricht bei jedem Fund ab |

Gebaut wird mit `TreatWarningsAsErrors=true` und `AnalysisMode=All`. Die
Sicherheitsanalyse (Roslyn-Analyzer, SecurityCodeScan) läuft damit in jedem Build mit.

---

## Technologie-Stack

| Technologie | Einsatz |
|---|---|
| .NET 10 / C# | Anwendungsframework |
| WinUI 3 (Windows App SDK 2.4) | UI-Framework |
| Windows SDK BuildTools 10.0.28000 | Manifest-Validierung, XAML-Compiler |
| Entity Framework Core 10 | ORM mit SQLite (WAL-Modus, Soft-Delete, Migrationen mit `VACUUM INTO`-Backup) |
| Microsoft.Extensions.Http.Resilience | Polly-basierte Retry-/Timeout-/Circuit-Breaker-Policies für HTTP-Provider |
| System.Security.Cryptography.ProtectedData | DPAPI-Verschlüsselung für Spotify-Credentials (CurrentUser-Scope) |
| TagLib# | Audio-Metadaten |
| xUnit | Testframework (eigene Fakes, kein Moq, kein Faker) |

---

## Architektur

Die Schichten hängen streng in eine Richtung voneinander ab:

```
EchoPlay.App                  → WinUI 3 UI, Composition Root, Dependency Injection
EchoPlay.Data                 → EF Core + SQLite, Entities, DataServices, Soft-Delete, SecureSettings (DPAPI), Einteilung gemeinsam/gerätegebunden
EchoPlay.Spotify              → Spotify Web API (Auth, Suche, Import, Scoring)
EchoPlay.AppleMusic           → iTunes Search API (Suche, Import, Scoring)
EchoPlay.LocalLibrary         → Lokale Bibliotheks-Integration (Scanner, Matcher, Cover-Suche)
EchoPlay.Logger               → Eigenes Logging-Framework (keine externen Pakete)
EchoPlay.Logger.Abstractions  → ILogger/ILoggerFactory-Interfaces (Domänen referenzieren diese, nicht die Logger-Implementierung)
EchoPlay.TagManager           → Audio-Tag-Editor (TagLib#, MusicBrainz-Lookup)
EchoPlay.Core                 → Fachkern, Heuristiken, Scoring-Interfaces, logische Uhr und fachliche Schlüssel für den Abgleich
EchoPlay.*.Tests              → Unit-, Integrations- und Smoke-Tests
EchoPlay.Fuzz                 → Property-based-Tests (FsCheck) für Parser und Redaktion
EchoPlay.Setup                → Inno-Setup-Skripte für den Windows-Installer
```

---

## Voraussetzungen

- Windows 10 (Build 19041) oder neuer
- .NET 10 SDK
- Visual Studio 2022 mit WinUI 3 Workload

---

## Schnellstart

```bash
git clone <repo-url>
cd EchoPlay
dotnet build EchoPlay.slnx
dotnet run --project src/EchoPlay.App
```

Mit lokalen Audiodateien funktioniert die App sofort. Für die Online-Suche über Spotify trägst du die Zugangsdaten im laufenden Programm unter **Einstellungen → Online** ein. Sie landen per Windows DPAPI (CurrentUser-Scope) verschlüsselt in der lokalen SQLite-Datenbank, also weder im Code noch in appsettings.json oder in User Secrets.

---

## Erste Schritte für Entwickler

1. **Solution öffnen:** `EchoPlay.slnx` in Visual Studio 2022. Startprojekt ist `EchoPlay.App`.
2. **Build-Plattform:** WinUI 3 und die Tests laufen nicht unter `AnyCPU`. Die Plattform kommt allerdings aus dem Projekt-Mapping in der `EchoPlay.slnx`, nicht von der Kommandozeile. **Kein `-p:Platform=x64` an die Solution geben:** Die `.slnx` kennt keine Konfiguration `Debug|x64` und bricht mit `MSB4126` ab. `dotnet build EchoPlay.slnx` genügt.
3. **Tests ausführen:** `dotnet test EchoPlay.slnx` für die ganze Solution oder einzeln je Projekt. Live-API-Tests (Spotify, iTunes) sind per Default skipped.
4. **Migrationen:** Nach einer Änderung an einer Entity `dotnet ef migrations add <Name> --project src/EchoPlay.Data --startup-project src/EchoPlay.App`. Die `.Designer.cs` muss mit committet werden, sonst erkennt EF die Migration nicht. Historie und Liste der Breaking Changes stehen in `MIGRATIONS.md`.
5. **Warnungen = Fehler:** Das Projekt baut mit `TreatWarningsAsErrors=true` und `AnalysisMode=All`. Eine Warnung wird gelöst. Abgeschaltet werden darf sie nur, wenn die Ursache nachweislich außerhalb des eigenen Codes liegt (Framework, generierter Code, Drittbibliothek): dann als `[SuppressMessage]` mit Begründung an der Methode, nie per `#pragma`. Ein Wächter-Test hält beides fest.
6. **DI-Lifetimes:** ViewModels sind `Transient`, `DbContext` ist `Scoped`. Für Datenbankzugriffe nutzen ViewModels `IServiceScopeFactory`. Einen `DbContext` direkt in ein ViewModel zu injizieren, wäre eine Captive Dependency und wird im Review abgelehnt.
7. **Keine Cover-BLOBs an Entities:** Cover liegen in der Tabelle `CoverImages` und werden über `ICoverImageDataService` referenziert.
8. **Registrierungen liegen bei ihrem Modul:** Der Container entsteht in `src/EchoPlay.App/Composition/`, mit je einer Erweiterungsmethode für Protokollierung, HTTP-Clients, Datenquellen, Anwendungsdienste und ViewModels. `AppHostFactory` legt nur die Reihenfolge fest. Neue Dienste gehören in die passende Datei, nicht in den Einstiegspunkt.
9. **Bindungen zeigen auf den zuständigen Bereich:** Seiten mit mehreren Bereichen (Startseite, Mediathek, Serienansicht, Einstellungen) binden direkt auf das ViewModel des Bereichs, also `ViewModel.ScanVM.IsScanning` und nicht `ViewModel.IsScanning`. Eine Eigenschaft zusätzlich auf der obersten Ebene zu führen, ist ausdrücklich unerwünscht. Weicht der weitergereichte Name nämlich ab, wartet die Bindung auf eine Meldung, die nie kommt, und die Anzeige bleibt stumm leer.
10. **Kommentare erklären das Warum:** Jeder Typ trägt einen XML-Kopf, der sagt, wofür es ihn gibt; jede gewählte Zahl steht mit ihrem Grund da. Ein Wächter-Test prüft die Köpfe im ganzen Quellbaum, auch an den Hilfsklassen der Tests.

---

## Tests

```bash
dotnet test
```

Alle Tests laufen ohne externe Abhängigkeiten. Smoke-Tests gegen echte APIs werden standardmäßig übersprungen und brauchen Netzwerkzugang.

Geschwindigkeit misst ein eigenes Projekt mit BenchmarkDotNet, getrennt vom Testlauf:

```bash
dotnet run -c Release --project tests/EchoPlay.Benchmarks -- --filter '*'
```

Es erzeugt einen Bestand in der Größe einer echten Mediathek aus einem festen Startwert und misst
die Abfragen von Startseite und Mediathek, den lokalen Scan, die Textarbeit bei Suche und Import,
das Laden und Dekodieren der Cover, den Datenbankstart samt Migrationen und das Protokoll. Mit
`--plans <Datei>` schreibt es stattdessen die Abfragepläne der gemessenen Abfragen.
Drei grobe Wächter im gewöhnlichen `dotnet test` schlagen an, wenn einer dieser Wege um ein
Vielfaches langsamer wird.

---

## Lizenz

MIT, siehe [LICENSE](LICENSE).

Mitgelieferte Schriften: Roboto Flex und Roboto Mono unter der SIL Open Font License 1.1, Lizenztexte
in `src/EchoPlay.App/Assets/Fonts/`.

---

**Stand:** .NET 10 / WinUI 3 (Windows App SDK 2.4), EF Core 10 / SQLite, 48 Migrationen.
