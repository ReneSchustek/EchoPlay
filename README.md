# EchoPlay

**Hörspiel-Verwaltung für Windows** – Serien entdecken, Episoden verfolgen, lokal und online.

EchoPlay ist eine Desktop-Anwendung für Hörspiel-Fans, die ihre Sammlung organisieren möchten. Serien können aus Online-Quellen (Spotify, Apple Music) importiert oder aus lokalen Audiodateien eingelesen werden. Die App merkt sich den Wiedergabestatus jeder Episode, zeigt Neuerscheinungen an und hilft beim Entdecken fehlender Folgen.

---

## Funktionsumfang

### Bibliothek

- **Online-Mediathek** – Serien aus Spotify und Apple Music durchsuchen, importieren und verwalten. Kachelansicht mit Cover, Favoriten-Stern und Überwachungs-Icon.
- **Lokale Mediathek** – Audiodateien scannen und automatisch zu Serien/Episoden zuordnen. Drei-Spalten-Navigation (Serien | Folgen | Tracks).
- **Nur-Online-Modus** – Für Nutzer ohne lokale Sammlung: lokale Mediathek komplett ausblendbar.

### Dashboard

- **Neuerscheinungen** – Neue Folgen überwachter Serien, monatlich gruppiert. Daten kommen live aus der iTunes-API.
- **Favoriten** – Schnellzugriff auf Lieblingsserien, per Drag & Drop sortierbar. Favorisieren schaltet die Neuerscheinungs-Überwachung gleich mit ein; abschalten lässt sie sich weiterhin einzeln über das Auge-Symbol. Überwachte Titel bleiben gemerkt: Nach einem Leeren der Mediathek bekommen gleichnamige Serien ihre Überwachung automatisch zurück.
- **Weiterhören** – Serien mit angefangenen, aber nicht abgeschlossenen Episoden.
- **Zuletzt gehört** – Die letzten Wiedergaben auf einen Blick.

### Wiedergabe

- Integrierter Audioplayer mit MiniPlayer, Zeitanzeige und Playlist-Unterstützung.
- 8 Audioformate: MP3, M4A, FLAC, OGG, WMA, WAV, AAC, Opus.
- Automatische Positionsspeicherung – beim nächsten Start wird an der letzten Stelle fortgesetzt.
- **Album beim Anbieter öffnen** – Folgen ohne lokale Datei lassen sich per Kontextmenü in Spotify oder Apple Music aufrufen. Ist die Spotify-App installiert, öffnet sie sich statt des Browsers; dort ist der Nutzer bereits angemeldet. Die Wiedergabe selbst steuert der Nutzer beim Anbieter — eine Fortsetzung wie bei lokalen Dateien gibt es dort nicht.

### Fehlende Folgen

- Die Lückensuche vergleicht die vorhandenen Ordner mit dem Bestand beim Anbieter und meldet, welche Nummern fehlen. Sie beginnt bei der kleinsten tatsächlich vorhandenen Folge, damit Serien, deren Sammlung erst später einsetzt, nicht als komplett unvollständig gelten.

### Tag-Manager

- Audio-Metadaten lesen und schreiben (TagLib#).
- Online-Lookup über MusicBrainz mit automatischer Erkennung aus der Ordnerstruktur.
- Batch-Tagging: gemeinsame Tags auf alle Dateien eines Ordners anwenden.
- Rekursive Ordnersuche und Mehrfachauswahl.
- Datei-Umbenennung nach konfigurierbarem Muster.

### Weitere Features

- **Cover-System** – 5 Online-Anbieter, lokaler Fallback, DB-Cache.
- **6 Themes** – Ruhrcoder, ModernClassic, PaperCoffee, MidnightLibrary, ForestSignal, AmberWhiskey.
- **Lokalisierung** – Deutsch und Englisch, zur Laufzeit umschaltbar.
- **Auto-Update** – Prüft beim Start auf neue Versionen via GitHub Releases. Der Download läuft nur über HTTPS von einem GitHub-Release-Host, und die Setup-Datei muss gegen den SHA-256-Hash aus dem Release-Body passen. Fehlt der Hash, wird nicht installiert.
- **Statistik** – Sammlungsübersicht, Hörfortschritt, Kennzahlen.
- **Kontexthilfe** – TeachingTips auf jeder Seite für neue Nutzer.

---

## Installation

Die fertige Anwendung gibt es als Windows-Installer unter
[GitHub Releases](https://github.com/ReneSchustek/EchoPlay/releases). Lade die
`EchoPlay-Setup-vX.Y.Z.exe` der neuesten Version herunter und führe sie aus — die
Installation läuft ohne Adminrechte (Per-User) und benötigt keine vorinstallierte
Runtime, da .NET und das Windows App SDK im Paket enthalten sind.

Die App prüft beim Start selbst auf neue Versionen und verifiziert heruntergeladene
Updates gegen den im Release hinterlegten SHA-256-Hash. Wie der Installer gebaut wird,
steht in [INSTALLER.md](INSTALLER.md), was sich je Version geändert hat in
[CHANGELOG.md](CHANGELOG.md).

Beim **manuellen** Download lohnt der Hash-Vergleich, weil das Setup nicht signiert ist
und SmartScreen deshalb warnt. Der erwartete Wert steht im Text des Releases:

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
| Dateizugriffe | Jede schreibende Stelle prüft über `SecurePathHelper.IsPathInside`, dass der Zielpfad im erlaubten Verzeichnis bleibt — auch nach Auflösung von `..` und Verknüpfungen |
| Links öffnen | Nur `http`/`https` oder ein exakt erwartetes Anwendungsschema; eine Adresse aus der Datenbank startet damit kein beliebiges Programm |
| Datenbankzugriff | EF Core mit LINQ; die wenigen Roh-SQL-Stellen nutzen ausschließlich feste Fragmente und Parameter |
| Protokolle | Verzeichnisse werden durch einen Kurz-Hash ersetzt, Geheimnisse in Abfrageparametern und Zugangsdaten in Adressen durch `***` |
| Abhängigkeiten | `dotnet list package --vulnerable`/`--deprecated` als hartes Gate, CodeQL und gitleaks in der CI |

Der Build läuft mit `TreatWarningsAsErrors=true` und `AnalysisMode=All`; die
Sicherheitsanalyse (Roslyn-Analyzer, SecurityCodeScan) ist damit Teil jedes Builds.

---

## Technologie-Stack

| Technologie | Einsatz |
|---|---|
| .NET 10 / C# | Anwendungsframework |
| WinUI 3 (Windows App SDK 2.3) | UI-Framework |
| Windows SDK BuildTools 10.0.28000 | Manifest-Validierung, XAML-Compiler |
| Entity Framework Core 10 | ORM mit SQLite (WAL-Modus, Soft-Delete, Migrationen mit `VACUUM INTO`-Backup) |
| Microsoft.Extensions.Http.Resilience | Polly-basierte Retry-/Timeout-/Circuit-Breaker-Policies für HTTP-Provider |
| System.Security.Cryptography.ProtectedData | DPAPI-Verschlüsselung für Spotify-Credentials (CurrentUser-Scope) |
| TagLib# | Audio-Metadaten |
| xUnit | Testframework (eigene Fakes, kein Moq, kein Faker) |

---

## Architektur

Strikte Schichtenarchitektur mit unidirektionalen Abhängigkeiten:

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

Die App funktioniert sofort mit lokalen Audiodateien. Für die Online-Suche über Spotify können die Zugangsdaten im laufenden Programm unter **Einstellungen → Online** eingegeben werden; sie werden per Windows DPAPI (CurrentUser-Scope) in der lokalen SQLite-Datenbank verschlüsselt abgelegt — weder im Code, in appsettings.json noch in User Secrets.

---

## Erste Schritte für Entwickler

1. **Solution öffnen:** `EchoPlay.slnx` in Visual Studio 2022. Startprojekt ist `EchoPlay.App`.
2. **Build-Plattform:** WinUI 3 und die Tests laufen nicht unter `AnyCPU` — die Plattform kommt aber aus dem Projekt-Mapping in der `EchoPlay.slnx`, nicht von der Kommandozeile. **Kein `-p:Platform=x64` an die Solution geben:** Die `.slnx` kennt keine Konfiguration `Debug|x64` und bricht mit `MSB4126` ab. `dotnet build EchoPlay.slnx` genügt.
3. **Tests ausführen:** `dotnet test EchoPlay.slnx` (Solution-weit) oder pro Projekt. Live-API-Tests (Spotify, iTunes) sind per Default skipped.
4. **Migrationen:** Nach Entity-Änderung `dotnet ef migrations add <Name> --project src/EchoPlay.Data --startup-project src/EchoPlay.App`. `.Designer.cs` muss committed werden — sonst erkennt EF die Migration nicht. Historie und Breaking-Change-Liste in `MIGRATIONS.md`.
5. **Warnungen = Fehler:** Das Projekt fährt mit `TreatWarningsAsErrors=true` und `AnalysisMode=All`. Neue CA-Warnungen müssen entweder gelöst oder mit Methoden-Begründung suppressed werden.
6. **DI-Lifetimes:** ViewModels sind `Transient`, `DbContext` ist `Scoped`. ViewModels nutzen `IServiceScopeFactory` für DB-Zugriff — direkte `DbContext`-Injektion in ViewModels ist ein Captive-Dependency-Muster und wird im Review abgelehnt.
7. **Keine Cover-BLOBs an Entities:** Cover liegen in der `CoverImages`-Tabelle, referenziert über `ICoverImageDataService`.

---

## Tests

```bash
dotnet test
```

Alle Tests laufen ohne externe Abhängigkeiten. Smoke-Tests gegen echte APIs sind standardmäßig übersprungen und erfordern Netzwerkzugang.

---

## Lizenz

Privates Projekt, keine öffentliche Lizenz.

---

**Stand:** .NET 10 / WinUI 3 (Windows App SDK 2.3), EF Core 10 / SQLite, 39 Migrationen.
