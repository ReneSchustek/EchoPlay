using EchoPlay.Data.Entities.Library;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Beschafft das Cover einer Serie und legt die Reihenfolge der Quellen fest.
    /// Eigener Typ, damit diese Reihenfolge für sich prüfbar ist — am Ansichtsmodell hing sie
    /// zuvor an der ganzen Seite.
    /// </summary>
    public interface ISeriesCoverBuilder
    {
        /// <summary>
        /// Beschafft das Cover einer Serie.
        /// </summary>
        /// <param name="series">Die Serie, deren Cover gesucht wird.</param>
        /// <returns>Das Bild oder <see langword="null"/>, wenn es keine Quelle gibt.</returns>
        /// <exception cref="System.IO.IOException">Die Cover-Datei ist nicht lesbar.</exception>
        /// <exception cref="System.UnauthorizedAccessException">Keine Leserechte auf die Cover-Datei.</exception>
        Task<BitmapImage?> BuildAsync(Series series);

        /// <summary>
        /// Wie <see cref="BuildAsync"/>, schluckt aber Lesefehler.
        /// Für das Nachladen im Hintergrund: Ein fehlendes Bild darf den Aufbau der Liste nicht
        /// stören — dort bleibt dann der Platzhalter stehen.
        /// </summary>
        /// <param name="series">Die Serie, deren Cover gesucht wird.</param>
        /// <returns>Das Bild oder <see langword="null"/>.</returns>
        Task<BitmapImage?> TryBuildAsync(Series series);
    }
}
