# EchoPlay

Windows-Desktop-Anwendung zur Verwaltung von Hörspielserien und -episoden.

---

## Zweck

EchoPlay ermöglicht das Suchen, Importieren und Verfolgen von Hörspielen aus verschiedenen Streaming-Diensten (Spotify, Apple Music) und lokalen Bibliotheken. Wiedergabestatus, Bibliothek und Einstellungen werden persistent in einer lokalen SQLite-Datenbank gespeichert.

---

## Technologie-Stack

| Technologie | Version |
|---|---|
| .NET | 10 |
| C# | Neueste Sprachversion |
| WinUI 3 | Windows App SDK 1.8 |
| Entity Framework Core | 10 (SQLite) |
| xUnit | Testframework |

Keine Mock-Frameworks – eigene Fakes statt Moq/NSubstitute.

---

## Voraussetzungen

- Windows 10 (19041) oder neuer
- .NET 10 SDK
- Visual Studio 2022 mit WinUI 3 Workload
- Spotify Developer App (Client ID + Secret) für Spotify-Funktionen

---

## Projekt aufsetzen

```bash
git clone <repo-url>
cd EchoPlay
```

Starten:

```bash
dotnet build -p:Platform=x64
dotnet run --project EchoPlay.App
```

Optional: Spotify-Zugangsdaten in `EchoPlay.App/appsettings.Development.json` hinterlegen, um die Online-Suche zu aktivieren. Die App funktioniert auch ohne Spotify-Anbindung.

---

## Tests ausführen

```bash
dotnet test
```

**Testergebnis:** 385+ Tests – 0 Fehler (App: 197, Data: 102, LocalLibrary: 86, Core: 37, Spotify: 42, AppleMusic: 30)

Smoke-Tests gegen echte APIs sind standardmäßig übersprungen (`[Fact(Skip = ...)]`). Diese erfordern Netzwerkzugang und gültige Credentials.

---

## Architekturübersicht

EchoPlay folgt einer strikten Schichtenarchitektur mit unidirektionalen Abhängigkeiten:

```
EchoPlay.App          → WinUI 3 UI, Composition Root, Dependency Injection
EchoPlay.Data         → EF Core + SQLite, Entities, DataServices, Soft-Delete
EchoPlay.Spotify      → Spotify Web API (Auth, Suche, Import, Scoring)
EchoPlay.AppleMusic   → iTunes Search API (Suche, Import, Scoring)
EchoPlay.LocalLibrary → Lokale Bibliotheks-Integration (Scanner, Matcher)
EchoPlay.Logger       → Eigenes Logging-Framework (keine externen Pakete)
EchoPlay.TagManager   → Audio-Tag-Editor (TagLib#, MusicBrainz-Lookup)
EchoPlay.Core         → Fachkern, Heuristiken, Scoring-Interfaces (keine Abhängigkeiten)
EchoPlay.*.Tests      → Unit-, Integrations- und Smoke-Tests
```

Detaillierte Architektur: [.ai/context/architecture.md](.ai/context/architecture.md)

---

## Entwicklungsstand

**Build:** 0 Fehler, 0 Warnungen

### Fertiggestellte Bereiche

- Eigenes Logging-Framework (Logger, MemorySink, Log-Viewer in Einstellungen)
- Datenpersistenz (EF Core + SQLite, Soft-Delete, Audit-Timestamps, Migrations)
- Spotify-Integration (Auth, Suche, Import, Hörspiel-Scoring)
- Apple-Music-Integration (Suche, Import, Scoring)
- Hörspiel-Erkennung (Heuristiken, Text-Normalisierung, Scoring)
- UI-Shell (NavigationView, MainWindow, StatusBar mit Scan-Fortschritt)
- Dashboard (Neuerscheinungen nach Serie gruppiert, Favoriten, Weiterhören, Läuft gerade, Zuletzt gehört, Online-Folgenprüfung via iTunes, grüner Haken bei gehörten Folgen)
- Online-Mediathek (Kachelansicht, Filter, Abonnement, Suche/Import, Serien-Überwachung, Favoriten-Stern, Überwachungs-Icon, Loading-Spinner)
- Lokale Mediathek (Drei-Spalten-Navigation: Serien | Folgen | Tracks; fehlende Folgen; Favoriten-Stern; Überwachungs-Icon; Als gehört markieren; Ordnerstruktur-Assistent; Filter/Sortierung; Lazy Cover-Loading)
- Nur-Online-Modus (lokale Mediathek ausblendbar für rein Online-Nutzer)
- Serienstatus-Seite (Episodenliste mit Fortschrittsbalken, Gesamtfortschritt, grüner Haken, Filter/Sortierung, Direkt-Playback)
- Einstellungen (Tabs: Allgemein, Online, Lokal, Verwaltung, Protokolle; Theme, Sprache, Sync, Cache-Management, DB-Pflege)
- Startup-Validierung (Online/Lokal-Check, Cache-Rebuild, Splash-Statustext)
- Kontexthilfe (TeachingTips auf jeder Seite)
- Audio-Wiedergabe (PlayerService, MediaPlaybackList, MiniPlayer mit Zeitanzeige, Playlist-Header, Direktwiedergabe aus Folgen-Panel, 8 Formate: MP3/M4A/FLAC/OGG/WMA/WAV/AAC/Opus)
- Import-Flow (ImportService, Suche, Import-Seite)
- Tag-Manager (AudioTag-Editor, MusicBrainz-Lookup, Auto-Lookup, Batch-Tagging, rekursive Ordnersuche, Mehrfachauswahl, Datei-Umbenennung)
- Cover-System (CoverImages-Tabelle, CoverService, BackgroundCoverService, SQL-Kopie, 5 Online-Anbieter)
- Theme-System (6 Themes: Ruhrcoder, ModernClassic, PaperCoffee, MidnightLibrary, ForestSignal, AmberWhiskey)
- Lokalisierung (DE/EN, .resw-Dateien, Sprachwechsel mit App-Neustart)
- Statistik-Seite (Sammlungsübersicht, Hörfortschritt, Kennzahlen)
- Abonnement-System (IsSubscribed-Flag, Dashboard-Integration)
### Offene Briefs

Keine offenen Briefs – alle Features implementiert.

---

## Coding-Regeln

- Kein `var` – immer explizite Typen
- Kommentare auf Deutsch, XML-Dokumentation für alle public/internal Members
- Keine Mock-Frameworks in Tests (eigene Fakes statt Moq)
- Architekturregeln sind verbindlich – keine neuen Schichten ohne Absprache

Vollständige Regeln: [CLAUDE.md](CLAUDE.md) und [.ai/rules/general.md](.ai/rules/general.md)

---

## Wichtige Dateien

| Datei | Inhalt |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Verbindliche Regeln für KI-Tools (Architektur, Code-Stil, Tests) |
| [.ai/status.md](.ai/status.md) | Aktueller Projektstand, offene Aufgaben |
| [.ai/context/architecture.md](.ai/context/architecture.md) | Detaillierte Architekturübersicht |
| [.ai/context/description.md](.ai/context/description.md) | Vollständige Projektbeschreibung |
| [.ai/rules/general.md](.ai/rules/general.md) | Strukturierte Coding-Regeln |

---

## Lizenz

Privates Projekt, keine öffentliche Lizenz.
