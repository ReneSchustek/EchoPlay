using EchoPlay.Data.Entities.Common;
using EchoPlay.Logger.Models;
using System;

namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Repräsentiert die anwendungsweiten Einstellungen.
    /// Wird als Singleton-Zeile in der Datenbank gespeichert.
    /// </summary>
    public class AppSettings : BaseEntity
    {
        /// <summary>
        /// Quelle für die Metadaten der Hörspiele.
        /// </summary>
        public ProviderType ActiveProvider { get; set; } = ProviderType.AppleMusic;

        /// <summary>
        /// Gibt an, ob lokale Dateien berücksichtigt werden sollen.
        /// </summary>
        public bool LocalLibraryEnabled { get; set; } = true;

        /// <summary>
        /// Lokaler Pfad zum Wurzelverzeichnis der Hörspielbibliothek.
        /// </summary>
        public string? LocalLibraryRootPath { get; set; }

        /// <summary>
        /// Namensmuster für die Ordner der Episoden.
        /// Unterstützte Platzhalter: {number}, {number:000}, {title}
        /// </summary>
        public string EpisodeFolderPattern { get; set; } = "{number:000} - {title}";

        /// <summary>
        /// Gibt an, ob Coverbilder zusätzlich als Datei im Episodenordner gespeichert werden sollen.
        /// </summary>
        public bool SaveCoverToDirectory { get; set; } = true;

        /// <summary>
        /// Name des aktiven Farbthemas.
        /// Gültige Werte: "MidnightLibrary", "ModernClassic", "PaperCoffee".
        /// </summary>
        public string ActiveTheme { get; set; } = "MidnightLibrary";

        /// <summary>
        /// BCP-47-Sprachcode der aktiven Benutzeroberflächen-Sprache.
        /// Gültige Werte: "de" (Deutsch), "en" (Englisch).
        /// </summary>
        public string ActiveLanguage { get; set; } = "de";

        /// <summary>
        /// Zuletzt im Player geöffneter Ordnerpfad.
        /// Wird beim nächsten Öffnen des FolderPickers als Startpfad vorgeschlagen.
        /// </summary>
        public string? LastOpenedPlayerFolder { get; set; }

        /// <summary>
        /// Anzahl der Tage, nach denen Log-Dateien automatisch gelöscht werden.
        /// Mindestwert: 1. Standard: 30 Tage.
        /// </summary>
        public int LogRetentionDays { get; set; } = 30;

        /// <summary>
        /// Mindest-Log-Level: Einträge unterhalb dieses Levels werden nicht geschrieben.
        /// Standard: <see cref="LogLevel.Information"/> – Debug- und Trace-Einträge bleiben stumm.
        /// </summary>
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gibt an, ob erkannte Serien nach einem Scan automatisch in die lokale Bibliothek
        /// importiert werden sollen. Nur Serien, die noch nicht existieren, werden angelegt –
        /// ohne Online-Suche oder Metadaten-Import aus externen Quellen.
        /// Standard: <see langword="true"/> – alle gefundenen Serien werden automatisch angelegt.
        /// </summary>
        public bool AutoImportAfterScan { get; set; } = true;

        /// <summary>
        /// Anzahl der Tage nach denen soft-gelöschte Einträge physisch aus der Datenbank entfernt werden.
        /// 0 bedeutet sofortige Bereinigung bei jedem App-Start.
        /// Standard: 30 Tage.
        /// </summary>
        public int DbPurgeDays { get; set; } = 30;

        /// <summary>
        /// Zeitpunkt des letzten App-Starts (UTC).
        /// Wird bei jedem Start aktualisiert und dient als Referenz für den
        /// Neuerscheinungen-Filter: nur Folgen, die seit <c>LastAppStart - <see cref="NewReleaseDays"/></c>
        /// erschienen sind, gelten als Neuerscheinung.
        /// </summary>
        public DateTime? LastAppStart { get; set; }

        /// <summary>
        /// Zeitfenster in Tagen für den Neuerscheinungen-Filter.
        /// Folgen mit einem Erscheinungsdatum innerhalb von <c>LastAppStart - NewReleaseDays</c>
        /// werden als Neuerscheinung auf dem Dashboard angezeigt.
        /// Gültiger Bereich: 7–365 Tage. Standard: 90 Tage. Konfigurierbar in Einstellungen → Allgemein.
        /// </summary>
        public int NewReleaseDays { get; set; } = 90;

        /// <summary>
        /// Offline-Modus: wenn aktiv, werden keine Online-Abfragen durchgeführt.
        /// Neuerscheinungen, iTunes-Prüfungen und Serien-Suche sind deaktiviert.
        /// Die lokale Mediathek (Serien, Folgen, Wiedergabe) funktioniert normal.
        /// Standard: <see langword="false"/> (Online-Funktionen aktiv).
        /// Konfigurierbar in Einstellungen → Allgemein.
        /// </summary>
        public bool OfflineMode { get; set; }

        /// <summary>
        /// Nur-Online-Modus: wenn aktiv, wird die lokale Mediathek ausgeblendet.
        /// Nur die Online-Mediathek ist verfügbar – für Nutzer ohne lokale Hörspielsammlung.
        /// Standard: <see langword="false"/> (lokale Mediathek sichtbar).
        /// Konfigurierbar in Einstellungen → Allgemein.
        /// </summary>
        public bool OnlineOnlyMode { get; set; }

        /// <summary>
        /// Versionsnummer die der Nutzer bewusst übersprungen hat.
        /// Wird vom Update-Dialog gesetzt und verhindert erneute Benachrichtigung für diese Version.
        /// Null bedeutet: keine Version übersprungen.
        /// </summary>
        public string? SkippedUpdateVersion { get; set; }

        /// <summary>
        /// Einmaliges Flag: wenn gesetzt, wird der Neuerscheinungen-Cache beim nächsten
        /// App-Start vollständig geleert und neu aufgebaut. Wird nach dem Leeren
        /// automatisch auf <see langword="false"/> zurückgesetzt.
        /// </summary>
        public bool ClearCacheOnNextStart { get; set; }

        /// <summary>
        /// Steuert, ob vor einer EF-Core-Migration ein Snapshot der Datenbankdatei angelegt wird.
        /// Standard: <see langword="true"/>. Wer auf knappen Datenträgern arbeitet oder das Backup
        /// extern übernimmt, kann die Funktion in den Einstellungen deaktivieren.
        /// </summary>
        public bool DbBackupEnabled { get; set; } = true;

        /// <summary>
        /// Anzahl der aufzubewahrenden DB-Backups vor Migrationen. Zulässiger Bereich 1–20.
        /// Standard: 5. Der Wert 0 wirkt wie eine Deaktivierung und kommt außerdem als
        /// Notfall-Schalter zum Einsatz, wenn der Nutzer die Opt-Out-Option noch nicht kennt.
        /// </summary>
        public int DbBackupRetentionCount { get; set; } = 5;
    }
}