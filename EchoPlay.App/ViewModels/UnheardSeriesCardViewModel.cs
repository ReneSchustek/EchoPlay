using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Kachel-ViewModel für den „Weiterhören"-Abschnitt der Startseite.
    /// Zeigt Serien an, bei denen der Nutzer mindestens eine Folge gehört hat,
    /// aber noch nicht alle – ein Hinweis zum Weiterhören.
    /// </summary>
    public sealed class UnheardSeriesCardViewModel
    {
        /// <summary>
        /// Erstellt eine Kachel für eine angefangene Serie mit ungehörten Folgen.
        /// </summary>
        /// <param name="seriesId">Datenbank-ID der Serie (für Navigation zur Detailseite).</param>
        /// <param name="seriesName">Titel der Serie.</param>
        /// <param name="coverImage">Serien-Cover oder null.</param>
        /// <param name="unheardCount">Anzahl der noch nicht gehörten Folgen.</param>
        public UnheardSeriesCardViewModel(
            Guid seriesId,
            string seriesName,
            BitmapImage? coverImage,
            int unheardCount)
        {
            SeriesId = seriesId;
            SeriesName = seriesName;
            CoverImage = coverImage;
            UnheardCount = unheardCount;

            // Singular/Plural korrekt
            DisplayText = unheardCount == 1
                ? "1 ungehörte Folge"
                : $"{unheardCount} ungehörte Folgen";
        }

        /// <summary>Datenbank-ID der Serie – für Navigation zur Detailseite.</summary>
        public Guid SeriesId { get; }

        /// <summary>Titel der Serie.</summary>
        public string SeriesName { get; }

        /// <summary>Serien-Cover oder null wenn keines vorhanden.</summary>
        public BitmapImage? CoverImage { get; }

        /// <summary>Anzahl der noch nicht gehörten Folgen.</summary>
        public int UnheardCount { get; }

        /// <summary>Anzeigetext, z.B. "12 ungehörte Folgen".</summary>
        public string DisplayText { get; }
    }
}
