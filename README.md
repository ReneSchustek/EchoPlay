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

---

## Voraussetzungen

- Windows 10 (19041) oder neuer
- .NET 10 SDK
- Visual Studio 2022 mit WinUI 3 Workload

---

## Projekt aufsetzen

```bash
git clone <repo-url>
cd EchoPlay
dotnet build -p:Platform=x64
dotnet run --project EchoPlay.App
```

Optional: Spotify-Zugangsdaten in `EchoPlay.App/appsettings.Development.json` hinterlegen, um die Online-Suche zu aktivieren. Die App funktioniert auch ohne Spotify-Anbindung.

---

## Tests ausführen

```bash
dotnet test
```

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

---

## Features

- Dashboard (Neuerscheinungen, Favoriten, Weiterhören, Zuletzt gehört)
- Online-Mediathek (Kachelansicht, Suche/Import, Serien-Überwachung, Favoriten)
- Lokale Mediathek (Drei-Spalten-Navigation, Scanner, Cover-Loading)
- Tag-Manager (AudioTag-Editor, MusicBrainz-Lookup, Batch-Tagging, rekursive Ordnersuche, Mehrfachauswahl, Datei-Umbenennung)
- Audio-Wiedergabe (8 Formate: MP3/M4A/FLAC/OGG/WMA/WAV/AAC/Opus)
- Cover-System (5 Online-Anbieter, lokaler Fallback, DB-Cache)
- Einstellungen (Theme, Sprache, Sync, Cache-Management, DB-Pflege)
- Lokalisierung (Deutsch/Englisch)
- Auto-Update (GitHub Releases API)

---

## Lizenz

Privates Projekt, keine öffentliche Lizenz.
