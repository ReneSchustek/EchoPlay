namespace EchoPlay.LocalLibrary.Models
{
    /// <summary>
    /// Beschreibt eine einzelne Verschiebe-Aktion für den Ordnerstruktur-Assistenten.
    /// Eine Audiodatei wird von ihrem aktuellen Speicherort in einen neuen Unterordner verschoben.
    /// </summary>
    public sealed class RestructureAction
    {
        /// <summary>Absoluter Pfad der Quelldatei (aktueller Speicherort).</summary>
        public required string SourcePath { get; init; }

        /// <summary>
        /// Absoluter Pfad des Zielordners, in den die Datei verschoben wird.
        /// Der Ordner wird erst beim Ausführen angelegt, nicht bei der Analyse.
        /// </summary>
        public required string TargetFolderPath { get; init; }

        /// <summary>Name des Zielordners (nur der Ordnername, kein Pfad). Für die Vorschau-Anzeige.</summary>
        public required string TargetFolderName { get; init; }

        /// <summary>Dateiname (ohne Pfad) für die Vorschau-Anzeige.</summary>
        public required string FileName { get; init; }

        /// <summary>Erkannte Episodennummer oder null wenn keine Nummer extrahiert werden konnte.</summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>Erkannter Episodentitel oder null.</summary>
        public string? EpisodeTitle { get; init; }
    }
}
