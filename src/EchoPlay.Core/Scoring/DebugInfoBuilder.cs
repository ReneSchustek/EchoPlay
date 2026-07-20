namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Sammelt bedingte Debug-Indikatoren und fügt sie zu einer lesbaren Zeile zusammen.
    /// Wird von den Hörspiel-Analyzern genutzt, um den „Indikator; Indikator; …"-Aufbau
    /// samt Fallback-Text zu vereinheitlichen.
    /// </summary>
    public sealed class DebugInfoBuilder
    {
        private readonly List<string> _parts = [];

        /// <summary>
        /// Fügt den Text hinzu, wenn die Bedingung zutrifft.
        /// </summary>
        /// <param name="condition">Ob der Indikator vorliegt.</param>
        /// <param name="text">Der beschreibende Text des Indikators.</param>
        public void Add(bool condition, string text)
        {
            if (condition)
            {
                _parts.Add(text);
            }
        }

        /// <summary>
        /// Baut die Debug-Zeile aus allen gesammelten Indikatoren; liefert den Fallback,
        /// wenn nichts gesammelt wurde.
        /// </summary>
        /// <param name="fallback">Text, der bei leerer Sammlung zurückgegeben wird.</param>
        /// <returns>Die zusammengesetzte Debug-Zeile.</returns>
        public string Build(string fallback) =>
            _parts.Count > 0 ? string.Join("; ", _parts) : fallback;
    }
}
