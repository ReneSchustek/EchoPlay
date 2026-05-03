using System.Collections.Generic;

namespace EchoPlay.Core.Models.Import
{
    /// <summary>
    /// Ergebnis eines Provider-Suchlaufs. Bündelt die Trefferliste mit dem Hinweis,
    /// ob der Suchlauf vom ursprünglich gewählten Provider auf Apple Music zurückfallen
    /// musste (nur relevant, wenn Spotify aktiv ist und keine Credentials hinterlegt sind).
    /// </summary>
    /// <param name="Results">Die gefundenen Serien/Alben des effektiv genutzten Providers.</param>
    /// <param name="SpotifyFallbackApplied">
    /// <see langword="true"/>, wenn der Nutzer Spotify als aktiven Provider gewählt hat,
    /// aber wegen fehlender Credentials Apple Music als Ersatz gelaufen ist.
    /// UI nutzt das Flag für einen einmaligen Hinweis pro Suche.
    /// </param>
    public sealed record SearchOutcome(
        IReadOnlyList<ImportSeries> Results,
        bool SpotifyFallbackApplied);
}
