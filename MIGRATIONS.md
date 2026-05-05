# EchoPlay — Datenbank-Migrationen

Historischer Überblick aller EF-Core-Migrationen für die lokale SQLite-Datenbank. Stand 2026-05-05, 36 Migrationen.

Jede Migration erzeugt drei Artefakte (Pflicht, siehe `memory.md` § EF-Core-Migration-Disziplin): `<Timestamp>_<Name>.cs`, `<Timestamp>_<Name>.Designer.cs`, aktualisierter `EchoPlayDbContextModelSnapshot.cs`. Der Pfad lautet `EchoPlay.Data/Migrations/`.

## Backup vor Migration

Seit Migration 34 (`AddDbBackupSettings`, 2026-04-16) legt `DatabaseInitializer.TryCreateBackupAsync` automatisch einen VACUUM-INTO-Snapshot in `db-backups/` an, bevor pending Migrationen ausgeführt werden. Opt-Out und Retention sind über `AppSettings.DbBackupEnabled` und `AppSettings.DbBackupRetentionCount` konfigurierbar. Rollback-Pfad: `runbook.md` §2 und §4.

## Breaking Changes für Major-Upgrades

| Migration | Wirkung | Was tun beim Upgrade |
|---|---|---|
| 28 — `AddCoverImagesTable` (2026-04-02) | Cover wandern von entity-gebundenen BLOB-Feldern (`Series.LocalCoverData`, `Episode.LocalCoverData`) in eine eigene `CoverImages`-Tabelle | Erst nach Migration 35 vollständig. Zwischenstände (v28–v34) haben beide Quellen parallel — UI nutzt `CoverService`. |
| 33 — `AddSourceHashSecureSettingsProviderIds` (2026-04-13) | Cover-Integrity per SHA-256, Spotify-Secrets als DPAPI-Blob, Series-ProviderIds strukturiert | Bestehende Spotify-Credentials werden beim ersten Start mit neuer Spalte migriert. DPAPI-Corruption-Recovery siehe `runbook.md` §1. |
| 35 — `MigrateLocalCoverDataToCoverImages` (2026-04-16) | Move-Migration: BLOB aus Entitäten → `CoverImages`-Tabelle, alte Spalten gelöscht | **Ohne Backup vor Migration potenziell zerstörerisch**: Migrationen 28–34 haben BLOB-Schatten; 35 löscht sie. `DatabaseInitializer`-Backup ist Pflicht-Vorbedingung. |

## Migrationen (chronologisch)

| # | Datum | Name | Zweck |
|---:|---|---|---|
| 1 | 2026-02-12 | InitialCreate | Grund-Schema: Series, Episode, Track, Entity-Basis |
| 2 | 2026-03-19 | AddAppSettings | Single-Row-Settings-Tabelle |
| 3 | 2026-03-19 | AddLocalLibraryFields | Bibliotheks-Root-Pfad, Scan-Flags |
| 4 | 2026-03-19 | AddActiveTheme | Theme-Auswahl (Light/Dark/System) |
| 5 | 2026-03-20 | AddActiveLanguage | Sprach-Auswahl (DE/EN) |
| 6 | 2026-03-20 | AddIsSubscribedToSeries | Serien-Abonnement-Flag |
| 7 | 2026-03-20 | AddLastOpenedPlayerFolder | Komfort-Feld für Player-Start |
| 8 | 2026-03-22 | AddLogRetentionDays | Log-Retention in AppSettings |
| 9 | 2026-03-23 | AddAutoImportAfterScan | Scan → automatisches Import-Toggle |
| 10 | 2026-03-23 | AddFolderPatternToSeries | Pro Serie eigenes Ordnermuster |
| 11 | 2026-03-23 | AddDbPurgeDays | Soft-Delete-TTL-Konfiguration |
| 12 | 2026-03-23 | AddIsFavoriteToSeries | Favoriten-Kennzeichnung |
| 13 | 2026-03-24 | AddEpisodeLocalCoverData | **BLOB-Spalte** (wird in Migration 35 entfernt) |
| 14 | 2026-03-25 | AddMinimumLogLevel | Log-Level-Konfiguration zur Laufzeit |
| 15 | 2026-03-28 | AddDashboardSortOrder | Manuelle Dashboard-Reihenfolge |
| 16 | 2026-03-28 | AddDashboardPositions | Drag-&-Drop-Positionen pro Kachel |
| 17 | 2026-03-29 | AddLastAppStart | Startup-Timestamp für Statistik |
| 18 | 2026-03-29 | AddNewReleaseDays | Horizont für „Neuerscheinungen" |
| 19 | 2026-03-30 | AddCachedNewReleases | Cache-Tabelle für iTunes-Checks |
| 20 | 2026-03-30 | AddOfflineMode | Offline-Schalter in Settings |
| 21 | 2026-03-30 | DatabaseOptimization | Indizes für häufige Queries |
| 22 | 2026-03-31 | AddIsOnlineImported | Import-Quelle pro Serie |
| 23 | 2026-03-31 | FixIsOnlineImported | Default-Wert-Korrektur |
| 24 | 2026-03-31 | CleanupTrackEpisodes | Dangling-Tracks entfernen |
| 25 | 2026-03-31 | AddProviderUrl | Provider-Deep-Link pro Episode |
| 26 | 2026-04-02 | AddCoverLastChecked | Cover-Refresh-Heuristik |
| 27 | 2026-04-02 | AddEpisodeCoverImageUrl | Episoden-Cover-URL separat |
| 28 | 2026-04-02 | AddCoverImagesTable | **Breaking:** neue `CoverImages`-Tabelle |
| 29 | 2026-04-02 | AddSeriesIsWatched | Gehört-Markierung auf Serien-Ebene |
| 30 | 2026-04-03 | AddClearCacheOnNextStart | Start-Flag für Cache-Reset |
| 31 | 2026-04-06 | AddOnlineOnlyMode | Online-Only-Toggle |
| 32 | 2026-04-06 | AddSkippedUpdateVersion | Übersprungene Versionen merken |
| 33 | 2026-04-13 | AddSourceHashSecureSettingsProviderIds | **Breaking:** SHA-256 + DPAPI + ProviderIds |
| 34 | 2026-04-16 | AddDbBackupSettings | Backup-Konfiguration in AppSettings |
| 35 | 2026-04-16 | MigrateLocalCoverDataToCoverImages | **Breaking:** BLOB-Spalten entfernen, Daten ausgelagert |
| 36 | 2026-05-05 | AddSortIndexesAndSoftDeleteFilters | Sortier-Indizes (`PlaybackStates.LastPlayedAt`, `Episodes.ReleaseDate`) plus `IsDeleted = 0`-Filter auf den UNIQUE-Indizes von `CoverImages` und `SecureSettings` |

## Prüf-Reflex vor jedem Migrations-Commit

```bash
ls EchoPlay.Data/Migrations/<Name>*
# muss ZWEI Dateien zeigen: <Name>.cs UND <Name>.Designer.cs
```

Fehlt die Designer-Datei, kennt EF Core die Migration nicht, `GetPendingMigrationsAsync()` liefert sie nicht, `MigrateAsync()` läuft leer durch — die App startet, jeder DB-Zugriff auf neue Spalten wirft erst zur Laufzeit `SQLite Error 1: no such column`. Weder Build noch Tests fangen das (siehe `memory.md` § Silent-Failure-Falle, aufgetreten 2026-04-13).

Fix: `dotnet ef migrations remove` + `dotnet ef migrations add <Name>` — dann liegen beide Dateien vor.

## Neue Migration anlegen

```bash
cd EchoPlay.Data
dotnet ef migrations add <MigrationName> --startup-project ../EchoPlay.App/EchoPlay.App.csproj
```

Danach Drei-Artefakt-Check, dann commit. Die Migration wird beim nächsten App-Start automatisch ausgeführt — `DatabaseInitializer` erzeugt vorher das VACUUM-INTO-Backup.
