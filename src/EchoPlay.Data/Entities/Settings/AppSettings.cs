using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;
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
        [DataScope(DataScope.Shared)]
        public ProviderType ActiveProvider { get; set; } = ProviderType.AppleMusic;

        /// <summary>
        /// Gibt an, ob lokale Dateien berücksichtigt werden sollen.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool LocalLibraryEnabled { get; set; } = true;

        /// <summary>
        /// Lokaler Pfad zum Wurzelverzeichnis der Hörspielbibliothek.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public string? LocalLibraryRootPath { get; set; }

        /// <summary>
        /// Namensmuster für die Ordner der Episoden.
        /// Unterstützte Platzhalter: {number}, {number:000}, {title}
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string EpisodeFolderPattern { get; set; } = "{number:000} - {title}";

        /// <summary>
        /// Gibt an, ob Coverbilder zusätzlich als Datei im Episodenordner gespeichert werden sollen.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool SaveCoverToDirectory { get; set; } = true;

        /// <summary>
        /// Name des aktiven Farbthemas. Die gültigen Namen kennt der Themendienst der App;
        /// ein unbekannter Name fällt dort auf den Standard zurück.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string ActiveTheme { get; set; } = "MidnightLibrary";

        /// <summary>
        /// BCP-47-Sprachcode der aktiven Benutzeroberflächen-Sprache.
        /// Gültige Werte: "de" (Deutsch), "en" (Englisch).
        /// </summary>
        [DataScope(DataScope.Shared)]
        public string ActiveLanguage { get; set; } = "de";

        /// <summary>
        /// Zuletzt im Player geöffneter Ordnerpfad.
        /// Wird beim nächsten Öffnen des FolderPickers als Startpfad vorgeschlagen.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public string? LastOpenedPlayerFolder { get; set; }

        /// <summary>
        /// Anzahl der Tage, nach denen Log-Dateien automatisch gelöscht werden.
        /// Mindestwert: 1. Standard: 30 Tage.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public int LogRetentionDays { get; set; } = 30;

        /// <summary>
        /// Mindest-Log-Level: Einträge unterhalb dieses Levels werden nicht geschrieben.
        /// Standard: <see cref="LogLevel.Information"/>; Debug- und Trace-Einträge bleiben stumm.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gibt an, ob erkannte Serien nach einem Scan automatisch in die lokale Bibliothek
        /// importiert werden sollen. Nur Serien, die noch nicht existieren, werden angelegt,
        /// ohne Online-Suche oder Metadaten-Import aus externen Quellen.
        /// Standard: <see langword="true"/>: Alle gefundenen Serien werden automatisch angelegt.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool AutoImportAfterScan { get; set; } = true;

        /// <summary>
        /// Anzahl der Tage nach denen soft-gelöschte Einträge physisch aus der Datenbank entfernt werden.
        /// 0 bedeutet sofortige Bereinigung bei jedem App-Start.
        /// Standard: 30 Tage.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public int DbPurgeDays { get; set; } = 30;

        /// <summary>
        /// Zeitpunkt des letzten App-Starts (UTC).
        /// Wird bei jedem Start aktualisiert und dient als Referenz für den
        /// Neuerscheinungen-Filter: nur Folgen, die seit <c>LastAppStart - <see cref="NewReleaseDays"/></c>
        /// erschienen sind, gelten als Neuerscheinung.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public DateTime? LastAppStart { get; set; }

        /// <summary>
        /// Ende des letzten vollständigen Aufbaus des Veröffentlichungskalenders (UTC).
        /// Gesetzt wird er erst, wenn alle Serien geprüft sind; ein abgebrochener Lauf
        /// lässt ihn stehen, und der nächste Start prüft erneut.
        /// </summary>
        [DataScope(DataScope.Cache)]
        public DateTime? ReleaseCalendarRefreshedAtUtc { get; set; }

        /// <summary>
        /// Zeitfenster in Tagen für den Neuerscheinungen-Filter.
        /// Folgen mit einem Erscheinungsdatum innerhalb von <c>LastAppStart - NewReleaseDays</c>
        /// werden als Neuerscheinung auf dem Dashboard angezeigt.
        /// Gültiger Bereich: 7–365 Tage. Standard: 90 Tage. Konfigurierbar in Einstellungen → Allgemein.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public int NewReleaseDays { get; set; } = 90;

        /// <summary>
        /// Offline-Modus: wenn aktiv, werden keine Online-Abfragen durchgeführt.
        /// Neuerscheinungen, iTunes-Prüfungen und Serien-Suche sind deaktiviert.
        /// Die lokale Mediathek (Serien, Folgen, Wiedergabe) funktioniert normal.
        /// Standard: <see langword="false"/> (Online-Funktionen aktiv).
        /// Konfigurierbar in Einstellungen → Allgemein.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool OfflineMode { get; set; }

        /// <summary>
        /// Nur-Online-Modus: wenn aktiv, wird die lokale Mediathek ausgeblendet.
        /// Nur die Online-Mediathek ist verfügbar, für Nutzer ohne lokale Hörspielsammlung.
        /// Standard: <see langword="false"/> (lokale Mediathek sichtbar).
        /// Konfigurierbar in Einstellungen → Allgemein.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool OnlineOnlyMode { get; set; }

        /// <summary>
        /// Versionsnummer die der Nutzer bewusst übersprungen hat.
        /// Wird vom Update-Dialog gesetzt und verhindert erneute Benachrichtigung für diese Version.
        /// Null bedeutet: keine Version übersprungen.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public string? SkippedUpdateVersion { get; set; }

        /// <summary>
        /// Einmaliges Flag: wenn gesetzt, wird der Neuerscheinungen-Cache beim nächsten
        /// App-Start vollständig geleert und neu aufgebaut. Wird nach dem Leeren
        /// automatisch auf <see langword="false"/> zurückgesetzt.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool ClearCacheOnNextStart { get; set; }

        /// <summary>
        /// Steuert, ob vor einer EF-Core-Migration ein Snapshot der Datenbankdatei angelegt wird.
        /// Standard: <see langword="true"/>. Wer auf knappen Datenträgern arbeitet oder das Backup
        /// extern übernimmt, kann die Funktion in den Einstellungen deaktivieren.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool DbBackupEnabled { get; set; } = true;

        /// <summary>
        /// Anzahl der aufzubewahrenden DB-Backups vor Migrationen. Zulässiger Bereich 1–20.
        /// Standard: 5. Der Wert 0 wirkt wie eine Deaktivierung und kommt außerdem als
        /// Notfall-Schalter zum Einsatz, wenn der Nutzer die Opt-Out-Option noch nicht kennt.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public int DbBackupRetentionCount { get; set; } = 5;

        /// <summary>
        /// Zuletzt gewählte Sortierung der Online-Folgenliste, damit die Auswahl über Serien-
        /// wechsel und App-Neustarts erhalten bleibt und nicht auf den Standard zurückspringt.
        /// 0 = Nummer aufsteigend (Standard), 1 = Nummer absteigend, 2 = Neueste zuerst.
        /// </summary>
        [DataScope(DataScope.Shared)]
        public int OnlineEpisodeSortIndex { get; set; }

        /// <summary>
        /// Zuletzt eingestellte Lautstärke der Wiedergabe, von 0,0 (still) bis 1,0 (voll).
        /// Standard: 1,0.
        /// </summary>
        /// <remarks>
        /// Sie überlebt den Neustart, weil eine leise eingestellte Anlage sonst bei jedem
        /// Start wieder auf volle Lautstärke springt. Beim abendlichen Hörspiel die
        /// unangenehmste Art von Überraschung.
        /// </remarks>
        [DataScope(DataScope.DeviceBound)]
        public double Volume { get; set; } = 1.0;

        /// <summary>
        /// Ob die Wiedergabe stummgeschaltet ist. Die Lautstärke bleibt dabei erhalten und
        /// gilt wieder, sobald die Stummschaltung endet.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public bool IsMuted { get; set; }

        /// <summary>
        /// Der gemeinsame Ordner für den Abgleich zwischen Geräten, etwa ein Ordner im Cloud-Speicher,
        /// oder leer. Gerätegebunden, weil derselbe Ordner auf jedem Rechner woanders eingebunden ist.
        /// </summary>
        [DataScope(DataScope.DeviceBound)]
        public string? ReplicationFolderPath { get; set; }

        /// <summary>Ob dieses Gerät über <see cref="ReplicationFolderPath"/> abgleicht.</summary>
        [DataScope(DataScope.DeviceBound)]
        public bool ReplicationEnabled { get; set; }
    }
}
