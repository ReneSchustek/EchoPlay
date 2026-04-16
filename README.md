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
- **Favoriten** – Schnellzugriff auf Lieblingsserien, per Drag & Drop sortierbar.
- **Weiterhören** – Serien mit angefangenen, aber nicht abgeschlossenen Episoden.
- **Zuletzt gehört** – Die letzten Wiedergaben auf einen Blick.

### Wiedergabe

- Integrierter Audioplayer mit MiniPlayer, Zeitanzeige und Playlist-Unterstützung.
- 8 Audioformate: MP3, M4A, FLAC, OGG, WMA, WAV, AAC, Opus.
- Automatische Positionsspeicherung – beim nächsten Start wird an der letzten Stelle fortgesetzt.

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
- **Auto-Update** – Prüft beim Start auf neue Versionen via GitHub Releases.
- **Statistik** – Sammlungsübersicht, Hörfortschritt, Kennzahlen.
- **Kontexthilfe** – TeachingTips auf jeder Seite für neue Nutzer.

---

## Technologie-Stack

| Technologie | Einsatz |
|---|---|
| .NET 10 / C# | Anwendungsframework |
| WinUI 3 (Windows App SDK 1.8) | UI-Framework |
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
dotnet build -p:Platform=x64
dotnet run --project EchoPlay.App
```

Die App funktioniert sofort mit lokalen Audiodateien. Für die Online-Suche über Spotify können die Zugangsdaten im laufenden Programm unter **Einstellungen → Online** eingegeben werden; sie werden per Windows DPAPI (CurrentUser-Scope) in der lokalen SQLite-Datenbank verschlüsselt abgelegt — weder im Code, in appsettings.json noch in User Secrets.

---

## Tests

```bash
dotnet test
```

Alle Tests laufen ohne externe Abhängigkeiten. Smoke-Tests gegen echte APIs sind standardmäßig übersprungen und erfordern Netzwerkzugang.

---

## Lizenz

Privates Projekt, keine öffentliche Lizenz.
