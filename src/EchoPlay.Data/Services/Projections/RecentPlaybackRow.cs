namespace EchoPlay.Data.Services.Projections
{
    /// <summary>
    /// Schmale Projektion für Dashboard-Ansichten („Zuletzt gehört" / „Weiterhören").
    /// Bewusst als <c>readonly record struct</c>, um die vollständige Materialisierung
    /// einer <c>PlaybackState</c>-Entität samt Audit-Feldern zu vermeiden, wenn nur die
    /// für Sortierung und Anzeige nötigen Spalten benötigt werden.
    /// </summary>
    /// <param name="Id">Primärschlüssel des Wiedergabestatus.</param>
    /// <param name="EpisodeId">Fremdschlüssel der zugehörigen Episode.</param>
    /// <param name="IsCompleted">Gibt an, ob die Episode vollständig gehört wurde.</param>
    /// <param name="LastPosition">Letzte bekannte Abspielposition.</param>
    /// <param name="SortKey">Vorberechneter Sortierschlüssel (LastPlayedAt ?? UpdatedAt ?? CreatedAt).</param>
    public readonly record struct RecentPlaybackRow(
        Guid Id,
        Guid EpisodeId,
        bool IsCompleted,
        TimeSpan LastPosition,
        DateTime SortKey);
}
