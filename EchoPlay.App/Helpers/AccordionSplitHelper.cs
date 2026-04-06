using System;
using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Berechnet die Aufteilung einer Kachelliste in Top- und Bottom-Bereich
    /// für das Akkordeon-Layout. Die Aufteilung erfolgt anhand der gewählten
    /// Kachel – das Akkordeon klappt direkt unterhalb der Reihe auf,
    /// in der die gewählte Kachel steht.
    /// </summary>
    public static class AccordionSplitHelper
    {
        /// <summary>
        /// Breite eines Serien-Kachel-Slots (Kachel + Margin).
        /// Zentral definiert – beide Mediathek-Pages nutzen denselben Wert.
        /// </summary>
        public const double SeriesTileSlotWidth = 148.0;

        /// <summary>
        /// Berechnet den Split-Index für die Aufteilung in Top/Bottom.
        /// </summary>
        /// <param name="selectedIndex">Index der gewählten Kachel in der Gesamtliste.</param>
        /// <param name="totalCount">Gesamtanzahl der Kacheln.</param>
        /// <param name="availableWidth">Verfügbare Breite in Pixel.</param>
        /// <returns>
        /// Index, an dem die Liste geteilt wird.
        /// Top = [0..splitIndex), Bottom = [splitIndex..totalCount).
        /// </returns>
        public static int CalculateSplitIndex(int selectedIndex, int totalCount, double availableWidth)
        {
            int tilesPerRow = CalculateTilesPerRow(availableWidth);
            int selectedRow = selectedIndex / tilesPerRow;
            return Math.Min((selectedRow + 1) * tilesPerRow, totalCount);
        }

        /// <summary>
        /// Berechnet die Anzahl der Kacheln pro Reihe anhand der verfügbaren Breite.
        /// </summary>
        /// <param name="availableWidth">Verfügbare Breite in Pixel.</param>
        /// <returns>Mindestens 1 Kachel pro Reihe.</returns>
        public static int CalculateTilesPerRow(double availableWidth)
        {
            return Math.Max(1, (int)(availableWidth / SeriesTileSlotWidth));
        }

        /// <summary>
        /// Teilt eine Liste an der berechneten Split-Position in zwei Teilbereiche.
        /// </summary>
        /// <typeparam name="T">Typ der Listenelemente.</typeparam>
        /// <param name="source">Die vollständige Liste.</param>
        /// <param name="splitIndex">Index, an dem geteilt wird.</param>
        /// <returns>Tupel aus Top- und Bottom-Liste.</returns>
        public static (IReadOnlyList<T> Top, IReadOnlyList<T> Bottom) Split<T>(
            IReadOnlyList<T> source, int splitIndex)
        {
            if (splitIndex <= 0) return (Array.Empty<T>(), source);
            if (splitIndex >= source.Count) return (source, Array.Empty<T>());

            T[] top = new T[splitIndex];
            T[] bottom = new T[source.Count - splitIndex];

            for (int i = 0; i < splitIndex; i++)
            {
                top[i] = source[i];
            }

            for (int i = splitIndex; i < source.Count; i++)
            {
                bottom[i - splitIndex] = source[i];
            }

            return (top, bottom);
        }
    }
}
