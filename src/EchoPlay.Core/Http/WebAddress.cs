namespace EchoPlay.Core.Http
{
    /// <summary>
    /// Liest eine Web-Adresse, die als Zeichenkette aus einer fremden Quelle kommt: einer
    /// Anbieter-Antwort, einer Datenbankspalte, einem Release-Feed.
    /// </summary>
    /// <remarks>
    /// An der Grenze einmal geprüft, fließt die Adresse danach als <see cref="Uri"/> durch die
    /// Anwendung. Ohne diese eine Stelle prüfte jeder Aufrufer selbst oder gar nicht, und ein
    /// relativer Pfad in einer Spalte, die „Url" heißt, endete als
    /// <see cref="UriFormatException"/> mitten in der Oberfläche.
    /// </remarks>
    public static class WebAddress
    {
        /// <summary>
        /// Liefert <paramref name="value"/> als absolute <c>http</c>- oder <c>https</c>-Adresse.
        /// </summary>
        /// <param name="value">Die Zeichenkette aus der fremden Quelle; darf leer sein.</param>
        /// <returns>
        /// Die Adresse, oder <see langword="null"/>, wenn der Wert leer, relativ, kein gültiger URI
        /// oder kein Web-Schema ist. Andere Schemata (<c>file</c>, App-Protokolle) scheiden aus,
        /// weil eine solche Adresse aus fremder Quelle beim Öffnen oder Laden ein beliebiges
        /// Ziel erreichen könnte.
        /// </returns>
        public static Uri? TryParse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || !Uri.TryCreate(value, UriKind.Absolute, out Uri? address))
            {
                return null;
            }

            return address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps
                ? address
                : null;
        }
    }
}
