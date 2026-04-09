namespace EchoPlay.App.Models
{
    /// <summary>
    /// Repräsentiert eine verfügbare Sprache für die Benutzeroberfläche.
    /// Wird in der Einstellungsseite als Datenelement für die Sprach-ComboBox verwendet.
    /// </summary>
    /// <param name="Code">BCP-47-Sprachcode, z.B. <c>"de"</c> oder <c>"en"</c>.</param>
    /// <param name="DisplayName">Anzeigename der Sprache, z.B. <c>"Deutsch"</c>.</param>
    public sealed record LanguageOption(string Code, string DisplayName);
}
