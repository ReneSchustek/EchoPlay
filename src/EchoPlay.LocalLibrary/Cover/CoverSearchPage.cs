using System;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Welcher Abschnitt der Trefferliste geholt werden soll. Seite 0 ist die erste Anfrage,
    /// jede weitere entsteht, wenn der Nutzer „Weitere Ergebnisse laden" wählt.
    /// </summary>
    /// <remarks>
    /// Ein eigener Typ statt eines <see langword="int"/>-Parameters, aus zwei Gründen: Der
    /// bestehende Aufruf <c>SearchAsync(titel, ct)</c> bleibt eindeutig — mit einem
    /// <see langword="int"/> hätte der Compiler das Abbruch-Token als Seitenzahl binden können.
    /// Und die Umrechnung Seite → Abschnitt liegt an einer Stelle statt in jedem Anbieter.
    /// </remarks>
    /// <param name="Index">Nullbasierte Seitennummer.</param>
    public readonly record struct CoverSearchPage(int Index)
    {
        /// <summary>Die erste Seite — das Verhalten aller Aufrufer vor Arbeitspaket 455.</summary>
        public static CoverSearchPage First => new(0);

        /// <summary>Die nächste Seite nach dieser.</summary>
        public CoverSearchPage Next => new(Index + 1);

        /// <summary>
        /// Rechnet die Seite in den Abschnitt um, den ein Anbieter abfragen muss.
        /// </summary>
        /// <param name="pageSize">Trefferzahl je Seite, die der Anbieter liefert.</param>
        /// <param name="supportsOffset">
        /// <see langword="true"/>, wenn die API einen Versatz kennt (MusicBrainz <c>offset</c>,
        /// Deezer <c>index</c>). <see langword="false"/> bei APIs, die nur eine Obergrenze
        /// kennen — dann wird mehr geholt und der Anfang verworfen, siehe
        /// <see cref="CoverSearchWindow.SkipLocally"/>.
        /// </param>
        /// <returns>Grenze und Versatz für die Anfrage.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Wenn <paramref name="pageSize"/> kleiner als 1 ist.</exception>
        public CoverSearchWindow ToWindow(int pageSize, bool supportsOffset)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

            if (supportsOffset)
            {
                return new CoverSearchWindow(pageSize, Index * pageSize, SkipLocally: 0);
            }

            // Ohne Versatz bleibt nur: die ersten n Seiten in einem Zug holen und den bereits
            // gezeigten Anfang wegwerfen. Kostet Datenvolumen, ist aber die einzige Variante,
            // die z. B. die iTunes Search API zulässt.
            return new CoverSearchWindow(pageSize * (Index + 1), Offset: 0, SkipLocally: Index * pageSize);
        }
    }

    /// <summary>
    /// Der konkrete Abschnitt einer Anbieter-Anfrage.
    /// </summary>
    /// <param name="Limit">Wie viele Treffer angefragt werden.</param>
    /// <param name="Offset">Versatz in der Anbieter-API; 0, wenn sie keinen kennt.</param>
    /// <param name="SkipLocally">
    /// Wie viele der gelieferten Treffer verworfen werden, weil sie schon gezeigt wurden.
    /// Nur bei APIs ohne Versatz größer als 0.
    /// </param>
    public readonly record struct CoverSearchWindow(int Limit, int Offset, int SkipLocally);
}
