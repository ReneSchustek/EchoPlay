namespace EchoPlay.Core.Models
{
    /// <summary>
    /// Stabile Kennung eines Dialogs, der sich dauerhaft ausblenden lässt.
    /// </summary>
    /// <remarks>
    /// Gespeichert wird der <b>Name</b> des Werts, nicht die Zahl — ein späteres Umsortieren
    /// dieser Aufzählung darf eine gemerkte Unterdrückung nicht auf einen anderen Dialog
    /// umhängen. Jeder Wert braucht in beiden Sprachdateien einen Eintrag
    /// <c>DialogName_&lt;Wert&gt;</c>; ein Test hält das nach.
    /// </remarks>
    public enum DialogKey
    {
        /// <summary>Kein Dialog. Nur Platzhalter — dieser Wert wird nie unterdrückt.</summary>
        None = 0,

        // ── Rückfragen (Ja/Abbrechen) ───────────────────────────────────────

        /// <summary>Vorhandenes Folgen-Cover überschreiben?</summary>
        EpisodeCoverOverwrite,

        /// <summary>Trotz Offline-Modus einmalig online gehen?</summary>
        OfflineOnlineAccess,

        /// <summary>Serie aus den Favoriten entfernen?</summary>
        FavoriteRemove,

        /// <summary>Serie aus der Mediathek entfernen?</summary>
        SeriesRemoveFromLibrary,

        /// <summary>Serie samt Dateien von der Platte löschen?</summary>
        SeriesDeleteFromDisk,

        /// <summary>Lokale Mediathek neu aufbauen?</summary>
        LibraryReinit,

        /// <summary>Folge als gehört markieren?</summary>
        EpisodeMarkPlayed,

        /// <summary>Folge wieder als ungehört markieren?</summary>
        EpisodeMarkUnplayed,

        /// <summary>Serie aus der Online-Mediathek entfernen?</summary>
        OnlineRemoveSeries,

        /// <summary>Serie auf Neuerscheinungen überwachen?</summary>
        OnlineSubscribe,

        /// <summary>Überwachung der Serie beenden?</summary>
        OnlineUnsubscribe,

        /// <summary>Einstellungsseite mit ungespeicherten Änderungen verlassen?</summary>
        UnsavedSettings,

        /// <summary>Für den Sprachwechsel neu starten?</summary>
        LanguageRestart,

        /// <summary>Cover auf alle Dateien des Ordners schreiben?</summary>
        TagManagerCoverApplyAll,

        /// <summary>Gefundene Angaben auf alle Dateien übernehmen?</summary>
        TagManagerApplyToAll,

        /// <summary>Dateien nach dem Muster umbenennen?</summary>
        TagManagerRename,

        /// <summary>Änderungen auf alle ausgewählten Dateien speichern?</summary>
        TagManagerSaveAll,

        /// <summary>Alle Tags der Datei entfernen?</summary>
        TagManagerRemoveAll,

        // ── Hinweise und Fehlermeldungen ────────────────────────────────────

        /// <summary>Cover konnte nicht geladen werden.</summary>
        CoverDownloadFailed,

        /// <summary>Suche im Offline-Modus nicht möglich.</summary>
        OfflineModeSearchHint,

        /// <summary>Lokale Mediathek im Nur-Online-Modus ausgeblendet.</summary>
        OnlineOnlyModeHint,

        /// <summary>Suche vor dem Import fehlgeschlagen.</summary>
        ImportSearchFailed,

        /// <summary>Import fehlgeschlagen.</summary>
        ImportFailed,

        /// <summary>Einlesen der lokalen Mediathek fehlgeschlagen.</summary>
        LibraryScanFailed,

        /// <summary>Neuaufbau der lokalen Mediathek fehlgeschlagen.</summary>
        LibraryReinitFailed,

        /// <summary>Abgleich der lokalen Mediathek fehlgeschlagen.</summary>
        LocalLibrarySyncFailed,

        /// <summary>Folge liegt noch nicht lokal vor.</summary>
        EpisodeNotAvailable,

        /// <summary>Folge lässt sich weder lokal noch beim Anbieter öffnen.</summary>
        EpisodeNotPlayable,

        /// <summary>Aktualisieren der Online-Serien fehlgeschlagen.</summary>
        OnlineRefreshFailed,

        /// <summary>Online-Suche fehlgeschlagen.</summary>
        OnlineSearchFailed,

        /// <summary>Import aus der Online-Suche fehlgeschlagen.</summary>
        OnlineImportFailed,

        /// <summary>Kein Anbieter eingerichtet, Online-Mediathek bleibt unsichtbar.</summary>
        NoProviderHint,

        /// <summary>Sprache gespeichert, Neustart muss von Hand erfolgen.</summary>
        LanguageRestartManual,

        /// <summary>Online-Abfrage lieferte keine Treffer.</summary>
        TagManagerNoResults,

        /// <summary>Cover konnte nicht entfernt werden.</summary>
        TagManagerCoverRemoveError,

        /// <summary>Ordner konnte nicht gelesen werden.</summary>
        TagManagerFolderLoadError,

        /// <summary>Tags konnten nicht gelesen werden.</summary>
        TagManagerTagLoadError,

        /// <summary>Online-Abfrage fehlgeschlagen.</summary>
        TagManagerLookupError,

        /// <summary>Selbsttätige Online-Abfrage fehlgeschlagen.</summary>
        TagManagerAutoLookupError,

        /// <summary>Vorschau der Umbenennung fehlgeschlagen.</summary>
        TagManagerPreviewError,

        /// <summary>Umbenennung nur teilweise gelungen.</summary>
        TagManagerRenamePartialError,

        /// <summary>Umbenennung fehlgeschlagen.</summary>
        TagManagerRenameError,

        /// <summary>Speichern der Tags fehlgeschlagen.</summary>
        TagManagerSaveError,

        /// <summary>Entfernen der Tags fehlgeschlagen.</summary>
        TagManagerRemoveTagsError,

        /// <summary>Cover ließ sich nicht auf alle Dateien schreiben.</summary>
        TagManagerCoverApplyAllError,

        /// <summary>Angaben ließen sich nicht auf alle Dateien übernehmen.</summary>
        TagManagerApplyToAllError
    }
}
