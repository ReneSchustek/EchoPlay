using System;

namespace EchoPlay.App.Models
{
    /// <summary>
    /// Repräsentiert eine Log-Datei in der Datei-Auswahl des Log-Viewers.
    /// </summary>
    /// <param name="FileName">Angezeigter Dateiname ohne Pfad.</param>
    /// <param name="Date">Datum der Log-Datei.</param>
    /// <param name="FilePath">Vollständiger Dateipfad. <see langword="null"/> für den Live-Modus.</param>
    public sealed record LogFileOption(string FileName, DateTime Date, string? FilePath);
}
