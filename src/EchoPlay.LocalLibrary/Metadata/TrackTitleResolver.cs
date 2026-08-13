using EchoPlay.Core.Logging;
using EchoPlay.Core.Parsing;
using EchoPlay.Logger.Abstractions;
using System.Security;

namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Liest die Titel einer Trackliste aus den Kennzeichnungen der Audiodateien.
    /// Gelesen wird erst beim Anzeigen, nicht beim Einlesen der Bibliothek: Der Titel steht
    /// allein in der Datei, und ein zusätzliches Feld in der Datenbank bliebe für alle
    /// Sammlungen leer, die nie neu eingelesen werden.
    /// </summary>
    public sealed class TrackTitleResolver : ITrackTitleResolver
    {
        private readonly ILogger _logger;
        private readonly ITagTitleReader _tagTitleReader;

        /// <summary>
        /// Initialisiert den Auflöser mit Logger-Fabrik und Tag-Leser.
        /// </summary>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        /// <param name="tagTitleReader">Liest Titel und Album aus einer Audiodatei.</param>
        public TrackTitleResolver(ILoggerFactory loggerFactory, ITagTitleReader tagTitleReader)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _logger = loggerFactory.CreateLogger(nameof(TrackTitleResolver));
            _tagTitleReader = tagTitleReader;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<string>> ResolveAsync(
            IReadOnlyList<TrackTitleRequest> tracks,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(tracks);

            if (tracks.Count == 0)
            {
                return [];
            }

            // TagLib# arbeitet synchron und öffnet jede Datei einzeln – auf einem Netzlaufwerk
            // dauert das spürbar. Der Aufrufer zeigt die Liste deshalb schon vorher mit dem
            // Dateinamen an und tauscht die Titel nach.
            return await Task.Run(() => ReadTitles(tracks, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Liest die Titel aller Spuren nacheinander. Parallel bringt nichts: Die Dateien
        /// liegen im selben Ordner, und der Flaschenhals ist das Laufwerk.
        /// </summary>
        private List<string> ReadTitles(IReadOnlyList<TrackTitleRequest> tracks, CancellationToken cancellationToken)
        {
            List<string> titles = new(tracks.Count);

            foreach (TrackTitleRequest track in tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                titles.Add(ReadTitle(track));
            }

            return titles;
        }

        /// <summary>
        /// Ermittelt den Anzeigenamen einer einzelnen Spur.
        /// Ist die Datei nicht lesbar, bleibt der aufgeräumte Dateiname stehen – die Liste
        /// darf an keiner Stelle leer bleiben.
        /// </summary>
        private string ReadTitle(TrackTitleRequest track)
        {
            try
            {
                (string title, string _) = _tagTitleReader.Read(track.FilePath);

                return TrackDisplayTitle.Choose(title, track.FilePath, track.TrackNumber);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or PathTooLongException
                                       or DirectoryNotFoundException
                                       or NotSupportedException
                                       or TagLib.CorruptFileException
                                       or TagLib.UnsupportedFormatException)
            {
                _logger.Warning(
                    "Titel nicht lesbar, Dateiname wird angezeigt: {Path} – {Reason}",
                    PathRedactor.Redact(track.FilePath),
                    ex.Message);

                return TrackDisplayTitle.FromFilePath(track.FilePath, track.TrackNumber);
            }
        }
    }
}
