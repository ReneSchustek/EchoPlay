namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Verwaltet die Datenbankpflege: Bereinigung veralteter Soft-Delete-Einträge,
    /// SQLite-Kompaktierung (VACUUM) und vollständiger Datenbank-Reset.
    /// </summary>
    public interface IDatabaseMaintenanceService
    {
        /// <summary>
        /// Löscht physisch alle Einträge, die seit mehr als <paramref name="retentionDays"/>
        /// Tagen als gelöscht markiert sind (<c>IsDeleted = true</c>).
        /// Die Reihenfolge respektiert FK-Einschränkungen: zuerst abhängige Entitäten
        /// (<see cref="EchoPlay.Data.Entities.Library.LocalTrack"/>,
        /// <see cref="EchoPlay.Data.Entities.Playback.PlaybackState"/>),
        /// dann Episoden und schließlich Serien.
        /// </summary>
        /// <param name="retentionDays">
        /// Anzahl Tage nach der Soft-Löschung, nach denen ein Eintrag physisch entfernt wird.
        /// 0 bedeutet sofortige Bereinigung aller soft-gelöschten Einträge.
        /// </param>
        /// <returns>Asynchrone Ausführung.</returns>
        Task PurgeAsync(int retentionDays);

        /// <summary>
        /// Kompaktiert die SQLite-Datenbankdatei.
        /// VACUUM schreibt die gesamte Datenbank um und gibt freigegebenen Speicher zurück –
        /// nur sinnvoll nach größeren Löschvorgängen oder auf expliziten Nutzerwunsch.
        /// Nicht für den App-Start empfohlen, da der Vorgang bei großen DBs spürbar dauern kann.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        Task VacuumAsync();

        /// <summary>
        /// Fordert SQLite auf, interne Statistiken zu aktualisieren und den Query-Planer zu optimieren.
        /// Sollte beim App-Shutdown aufgerufen werden – die gesammelten Statistiken einer Sitzung
        /// verbessern die Abfrageplanung beim nächsten Start.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        Task OptimizeAsync();

        /// <summary>
        /// Löscht alle Bibliotheksdaten aus der Datenbank: Serien, Episoden,
        /// Wiedergabestände und lokale Track-Zuordnungen.
        /// Einstellungen (<see cref="EchoPlay.Data.Entities.Settings.AppSettings"/>) bleiben erhalten.
        /// </summary>
        /// <remarks>
        /// Diese Methode führt einen Hard-Delete durch und umgeht den globalen
        /// Soft-Delete-Filter (<c>IgnoreQueryFilters</c> + <c>ExecuteDeleteAsync</c>).
        /// Begründung: „Bibliothek leeren" ist ein expliziter Nutzer-Befehl mit der Erwartung,
        /// dass die Tabellen anschließend leer sind. Soft-Delete-Reste würden beim
        /// nächsten Import den Eindeutigkeits-Hash blockieren und neue Treffer verwerfen.
        /// </remarks>
        Task ClearLibraryAsync();

        /// <summary>
        /// Löscht nur online-importierte Serien und deren abhängige Daten
        /// (Episoden, PlaybackStates, LocalTracks).
        /// Lokale Serien und Einstellungen bleiben erhalten.
        /// </summary>
        /// <remarks>
        /// Hard-Delete analog zu <see cref="ClearLibraryAsync"/>: Soft-Delete-Bypass ist
        /// notwendig, weil ein erneuter Online-Import sonst über soft-gelöschte Datensätze
        /// stolpert. Nur online-importierte Serien sind betroffen — lokale Bibliothek bleibt
        /// unberührt.
        /// </remarks>
        Task ClearOnlineLibraryAsync();

        /// <summary>
        /// Entfernt alle lokalen Verknüpfungen: setzt <c>LocalFolderPath</c> auf null,
        /// löscht alle <see cref="EchoPlay.Data.Entities.Library.LocalTrack"/>-Einträge
        /// und entfernt rein lokale Serien (nicht online-importiert).
        /// Online-importierte Serien und Einstellungen bleiben erhalten.
        /// </summary>
        /// <remarks>
        /// Hard-Delete für rein lokale Serien (Soft-Delete-Bypass): nach „Lokale Bibliothek
        /// trennen" muss der nächste Scan dieselben Pfade wieder neu anlegen können — Soft-Delete-
        /// Reste würden den Re-Scan blockieren.
        /// </remarks>
        Task ClearLocalLibraryAsync();
    }
}
