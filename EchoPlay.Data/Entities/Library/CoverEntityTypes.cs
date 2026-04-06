namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Definiert die gültigen EntityType-Werte für die CoverImages-Tabelle.
    /// Zentraler Ort für die Konstanten, damit keine String-Literale verstreut im Code liegen.
    /// </summary>
    public static class CoverEntityTypes
    {
        /// <summary>Entity-Typ für Serien-Cover.</summary>
        public const string Series = "Series";

        /// <summary>Entity-Typ für Episoden-Cover.</summary>
        public const string Episode = "Episode";
    }
}
