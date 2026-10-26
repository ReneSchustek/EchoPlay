# EchoPlay

**Hörspiel-Verwaltung für Windows:** Serien entdecken, Episoden verfolgen, lokal und online.

EchoPlay ist eine Desktop-Anwendung für alle, die ihre Hörspielsammlung in Ordnung halten wollen. Serien lassen sich aus Online-Quellen (Spotify, Apple Music) importieren oder aus lokalen Audiodateien einlesen. Die App merkt sich, wie weit jede Episode gehört ist, zeigt Neuerscheinungen an und hilft dabei, fehlende Folgen zu finden.

---

## Funktionsumfang

### Bibliothek

- **Online-Mediathek.** Serien aus Spotify und Apple Music durchsuchen, importieren und verwalten. Die Kachelansicht zeigt Cover, Favoriten-Stern und Überwachungs-Icon.
- **Lokale Mediathek.** Audiodateien werden gescannt und automatisch Serien und Episoden zugeordnet. Navigiert wird in drei Spalten (Serien | Folgen | Tracks).
- **Nur-Online-Modus.** Wer keine lokale Sammlung hat, blendet die lokale Mediathek einfach komplett aus.
- **Buchstaben-Abschnitte mit Sprungleiste.** Beide Mediatheken gliedern ihre Serien nach Anfangsbuchstaben. Die Leiste darüber filtert nicht, sie springt zum gewählten Buchstaben, und der übrige Bestand bleibt sichtbar. Buchstaben ohne Einträge sind erkennbar, lassen sich aber nicht anwählen. In der Online-Mediathek erscheint die Gliederung nur bei Sortierung nach Namen, denn nach Folgenanzahl sortiert gäbe es keine Buchstabenordnung, auf die eine Sprungmarke zeigen könnte.
- **Folgenbereich zuklappen ohne Zurückscrollen.** Ist eine Serie aufgeklappt, steht unten rechts ein Knopf, der beim Scrollen mitwandert. Er schließt den Bereich und holt die Serie zurück ins Blickfeld, statt einen irgendwo in der Liste stehen zu lassen.

### Dashboard

- **Neuerscheinungen.** Neue Folgen überwachter Serien, nach Monaten gruppiert. Die Daten kommen live aus der iTunes-API.
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

### Tag-Manager

- Audio-Metadaten lesen und schreiben (TagLib#).
- Online-Lookup über MusicBrainz, der die Angaben automatisch aus der Ordnerstruktur erkennt.
- Batch-Tagging: gemeinsame Tags auf alle Dateien eines Ordners anwenden.
- Rekursive Ordnersuche und Mehrfachauswahl.
- Dateien nach einem konfigurierbaren Muster umbenennen.

### Weitere Features

- **Suche und Filter auf jeder Listenseite.** Mediatheken, Serien-Detail, Suche, Protokoll und Tag-Manager sind gleich aufgebaut: Seitenkopf, Suchfeld, Filter als Chips. Gesucht wird schon beim Tippen. Findet die Suche nichts, sagt die Seite das anders als bei leerem Bestand und bietet den Weg zurück zum vollständigen Bestand an. Such- und Filtereinstellungen überstehen den Wechsel auf eine andere Seite und zurück.
- **Anbieter-Suche mit laufender Anzeige.** Die Treffer erscheinen einzeln, sobald sie feststehen, und nicht erst nach dem vollständigen Suchlauf; jede Kachel bringt ihr Cover gleich mit. Künstler ohne Bezug zu Namen oder Genre scheiden vor der aufwendigen Prüfung aus, bekannte Serien werden gar nicht erst geprüft. Wer während einer laufenden Suche weitertippt, bricht sie ab, ohne dass daraus Fehlermeldungen entstehen.
- **Cover-System.** 5 Online-Anbieter, lokaler Fallback, DB-Cache. Die Bilder kommen von einem Auslieferungsnetz und laden deshalb nebeneinander statt hintereinander; die Cover einer Trefferseite stehen zusammen.
- **6 Themes.** Ruhrcoder, ModernClassic, PaperCoffee, MidnightLibrary, ForestSignal, AmberWhiskey. Aufbau, Kachelform und Abstände sind in allen Themes gleich, nur die Farbe bestimmt das gewählte Theme. Text hat in jedem Theme einen Kontrast von mindestens 4,5:1.
- **Lokalisierung.** Deutsch und Englisch, zur Laufzeit umschaltbar.
- **Auto-Update.** Beim Start sucht die App über GitHub Releases nach neuen Versionen. Heruntergeladen wird nur über HTTPS von einem GitHub-Release-Host, und die Setup-Datei muss zum SHA-256-Hash aus dem Release-Text passen. Fehlt der Hash, wird nicht installiert.
- **Statistik.** Sammlungsübersicht, Hörfortschritt, Kennzahlen.
- **Kontexthilfe.** TeachingTips auf jeder Seite für neue Nutzer.
- **Hinweise dauerhaft ausblenden.** Jeder Hinweis und jede Rückfrage hat ein Häkchen „nicht wieder anzeigen". Bei Rückfragen merkt sich die App nur eine Zustimmung; wer abbricht, wird beim nächsten Mal wieder gefragt. Unerwartete Fehler und ein fehlgeschlagener Start sind davon ausgenommen, die müssen sichtbar bleiben. Unter Einstellungen → Verwaltung stehen alle ausgeblendeten Hinweise mit Datum und lassen sich einzeln oder gesammelt zurückholen.

---

## Installation

Die fertige Anwendung gibt es als Windows-Installer unter
[GitHub Releases](https://github.com/ReneSchustek/EchoPlay/releases). Lade die
`EchoPlay-Setup-vX.Y.Z.exe` der neuesten Version herunter und führe sie aus. Die
Installation braucht keine Adminrechte (Per-User) und auch keine vorinstallierte
Runtime, weil .NET und das Windows App SDK im Paket enthalten sind.

Beim Start sieht die App selbst nach neuen Versionen und prüft heruntergeladene
Updates gegen den SHA-256-Hash, der im Release hinterlegt ist.

Lädst du das Setup **von Hand** herunter, lohnt sich der Hash-Vergleich: Das Setup ist
nicht signiert, deshalb warnt SmartScreen. Den erwarteten Wert findest du im Text des
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
| Auto-Update | HTTPS-Zwang, Host-Bindung an GitHub-Release-Hosts, SHA-256-Pflichtprüfung mit zeitkonstantem Vergleich, Versionsformat als Whitelist |
| Spotify-Zugangsdaten | Windows DPAPI im `CurrentUser`-Scope mit anwendungsspezifischer Entropie; nie im Code, in `appsettings.json` oder in User Secrets |
| Dateizugriffe | Jede schreibende Stelle prüft über `SecurePathHelper.IsPathInside`, dass der Zielpfad im erlaubten Verzeichnis bleibt, auch nach Auflösung von `..` und Verknüpfungen |
| Links öffnen | Nur `http`/`https` oder ein exakt erwartetes Anwendungsschema; eine Adresse aus der Datenbank startet damit kein beliebiges Programm |
| Datenbankzugriff | EF Core mit LINQ; die wenigen Roh-SQL-Stellen nutzen ausschließlich feste Fragmente und Parameter |
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
EchoPlay.Data                 → EF Core + SQLite, Entities, DataServices, Soft-Delete, SecureSettings (DPAPI)
EchoPlay.Spotify              → Spotify Web API (Auth, Suche, Import, Scoring)
EchoPlay.AppleMusic           → iTunes Search API (Suche, Import, Scoring)
EchoPlay.LocalLibrary         → Lokale Bibliotheks-Integration (Scanner, Matcher, Cover-Suche)
EchoPlay.Logger               → Eigenes Logging-Framework (keine externen Pakete)
EchoPlay.Logger.Abstractions  → ILogger/ILoggerFactory-Interfaces (Domänen referenzieren diese, nicht die Logger-Implementierung)
EchoPlay.TagManager           → Audio-Tag-Editor (TagLib#, MusicBrainz-Lookup)
EchoPlay.Core                 → Fachkern, Heuristiken, Scoring-Interfaces
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
5. **Warnungen = Fehler:** Das Projekt baut mit `TreatWarningsAsErrors=true` und `AnalysisMode=All`. Neue CA-Warnungen werden entweder gelöst oder an der Methode mit Begründung unterdrückt.
6. **DI-Lifetimes:** ViewModels sind `Transient`, `DbContext` ist `Scoped`. Für Datenbankzugriffe nutzen ViewModels `IServiceScopeFactory`. Einen `DbContext` direkt in ein ViewModel zu injizieren, wäre eine Captive Dependency und wird im Review abgelehnt.
7. **Keine Cover-BLOBs an Entities:** Cover liegen in der Tabelle `CoverImages` und werden über `ICoverImageDataService` referenziert.
8. **Registrierungen liegen bei ihrem Modul:** Der Container entsteht in `src/EchoPlay.App/Composition/`, mit je einer Erweiterungsmethode für Protokollierung, HTTP-Clients, Datenquellen, Anwendungsdienste und ViewModels. `AppHostFactory` legt nur die Reihenfolge fest. Neue Dienste gehören in die passende Datei, nicht in den Einstiegspunkt.
9. **Bindungen zeigen auf den zuständigen Bereich:** Seiten mit mehreren Bereichen (Startseite, Mediathek, Serienansicht, Einstellungen) binden direkt auf das ViewModel des Bereichs, also `ViewModel.ScanVM.IsScanning` und nicht `ViewModel.IsScanning`. Eine Eigenschaft zusätzlich auf der obersten Ebene zu führen, ist ausdrücklich unerwünscht. Weicht der weitergereichte Name nämlich ab, wartet die Bindung auf eine Meldung, die nie kommt, und die Anzeige bleibt stumm leer.

---

## Tests

```bash
dotnet test
```

Alle Tests laufen ohne externe Abhängigkeiten. Smoke-Tests gegen echte APIs werden standardmäßig übersprungen und brauchen Netzwerkzugang.

---

## Lizenz

MIT, siehe [LICENSE](LICENSE).

---

**Stand:** .NET 10 / WinUI 3 (Windows App SDK 2.4), EF Core 10 / SQLite, 43 Migrationen.
