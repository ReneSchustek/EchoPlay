using System.Diagnostics.CodeAnalysis;
using EchoPlay.Logger.Abstractions;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;

namespace EchoPlay.TagManager.Services
{
    /// <summary>
    /// Liest und schreibt Audio-Metadaten über TagLib#.
    /// TagLib# unterstützt alle gängigen Audioformate (MP3/ID3, FLAC/Vorbis, OGG, AAC, WMA, WAV).
    /// Da TagLib# die Datei synchron auf dem aufrufenden Thread öffnet, werden alle
    /// I/O-Operationen in <c>Task.Run</c> ausgeführt, damit der UI-Thread frei bleibt.
    /// </summary>
    internal sealed class TagService : ITagService
    {
        /// <summary>Unterstützte Dateiendungen für <see cref="ReadFolderAsync"/>.</summary>
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".wav", ".opus", ".ape", ".mpc"
        };

        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den <see cref="TagService"/> mit einem dedizierten Logger-Kanal.
        /// </summary>
        /// <param name="loggerFactory">Factory, die den Logger für diesen Dienst erstellt.</param>
        public TagService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(nameof(TagService));
        }

        /// <inheritdoc />
        public async Task<AudioTag> ReadAsync(string filePath)
        {
            _logger.Debug($"Lese Tags: {filePath}");

            return await Task.Run(() =>
            {
                try
                {
                    using TagLib.File file = OpenFile(filePath);

                    AudioTag tag = MapToAudioTag(file.Tag);
                    _logger.Debug($"Tags gelesen – Format: {file.MimeType}, Titel: {tag.Title ?? "(kein)"}");
                    return tag;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Lesen der Tags: {filePath}", ex);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task WriteAsync(string filePath, AudioTag tag)
        {
            await Task.Run(() =>
            {
                try
                {
                    using TagLib.File file = OpenFile(filePath);

                    ApplyAudioTag(file.Tag, tag);
                    file.Save();

                    _logger.Info($"Tags geschrieben: {filePath}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Schreiben der Tags: {filePath}", ex);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task WriteCoverAsync(string filePath, byte[]? imageData, string mimeType = "image/jpeg")
        {
            await Task.Run(() =>
            {
                try
                {
                    using TagLib.File file = OpenFile(filePath);

                    if (imageData is null)
                    {
                        // Null bedeutet: vorhandenes Cover entfernen
                        file.Tag.Pictures = [];
                        _logger.Info($"Cover entfernt: {filePath}");
                    }
                    else
                    {
                        TagLib.Picture picture = new()
                        {
                            Data = new TagLib.ByteVector(imageData),
                            MimeType = mimeType,
                            Type = TagLib.PictureType.FrontCover
                        };
                        file.Tag.Pictures = [picture];
                        _logger.Info($"Cover geschrieben ({mimeType}, {imageData.Length} Bytes): {filePath}");
                    }

                    file.Save();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Schreiben des Covers: {filePath}", ex);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RemoveAllTagsAsync(string filePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    using TagLib.File file = OpenFile(filePath);

                    file.Tag.Clear();
                    file.Save();

                    _logger.Info($"Alle Tags entfernt: {filePath}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Fehler beim Entfernen aller Tags: {filePath}", ex);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Resilience-Pattern: TagLib# wirft aus nativen Parsern diverse Exceptions (CorruptFileException, UnsupportedFormatException, IOException, UnauthorizedAccessException). Eine einzelne korrupte Datei darf die Verarbeitung des restlichen Ordners nicht stoppen.")]
        public async Task<IReadOnlyList<(string FilePath, AudioTag Tag)>> ReadFolderAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"Ordner nicht gefunden: {folderPath}");
            }

            // Rekursive Suche: alle Unterordner einbeziehen
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _logger.Debug($"ReadFolderAsync: {files.Length} unterstützte Dateien in {folderPath}");

            List<(string, AudioTag)> results = new(files.Length);

            foreach (string file in files)
            {
                try
                {
                    AudioTag tag = await ReadAsync(file).ConfigureAwait(false);
                    results.Add((file, tag));
                }
                catch (Exception ex)
                {
                    // Bewusst breiter Catch: TagLib# wirft diverse Exceptions
                    // (CorruptFileException, UnsupportedFormatException, IOException etc.).
                    // Einzelne Dateien überspringen, damit der Rest des Ordners verarbeitet wird.
                    _logger.Warning($"Datei übersprungen (Fehler beim Tag-Lesen): {file} – {ex.Message}");
                }
            }

            return results;
        }

        // --- Hilfsmethoden ---

        /// <summary>
        /// Öffnet eine Datei mit TagLib# und wandelt <see cref="TagLib.UnsupportedFormatException"/>
        /// in eine sprechende <see cref="InvalidOperationException"/> um.
        /// </summary>
        /// <param name="filePath">Absoluter Dateipfad.</param>
        /// <returns>Geöffnetes <see cref="TagLib.File"/>-Objekt.</returns>
        /// <exception cref="InvalidOperationException">
        /// Wird geworfen, wenn das Dateiformat von TagLib# nicht unterstützt wird.
        /// </exception>
        private static TagLib.File OpenFile(string filePath)
        {
            try
            {
                return TagLib.File.Create(filePath);
            }
            catch (TagLib.UnsupportedFormatException ex)
            {
                throw new InvalidOperationException(
                    $"Das Dateiformat wird von TagLib# nicht unterstützt: {Path.GetExtension(filePath)}",
                    ex);
            }
        }

        /// <summary>
        /// Wandelt ein TagLib-Tag-Objekt in ein <see cref="AudioTag"/>-DTO um.
        /// Leere Strings werden als <see langword="null"/> zurückgegeben, damit der Aufrufer
        /// sicher auf <c>tag.Title is null</c> prüfen kann statt auf leere Strings.
        /// </summary>
        private AudioTag MapToAudioTag(TagLib.Tag source)
        {
            // Cover: nur das erste Bild (FrontCover) übernehmen; bei mehreren Bildern
            // ist das erste üblicherweise das Albumcover.
            byte[]? coverData = null;
            string? coverMime = null;

            if (source.Pictures.Length > 0)
            {
                TagLib.IPicture pic = source.Pictures[0];
                coverData = pic.Data.Data;
                coverMime = pic.MimeType;
            }
            else
            {
                _logger.Warning("Keine Cover-Daten gefunden – Feld bleibt null");
            }

            return new AudioTag
            {
                Title = NullIfEmpty(source.Title),
                Album = NullIfEmpty(source.Album),
                // TagLib liefert Artist und AlbumArtist als Arrays (mehrere Künstler möglich).
                // Wir fassen sie mit "; " zusammen, wie es in der Praxis üblich ist.
                Artist = NullIfEmpty(string.Join("; ", source.Performers)),
                AlbumArtist = NullIfEmpty(string.Join("; ", source.AlbumArtists)),
                Comment = NullIfEmpty(source.Comment),
                Genre = NullIfEmpty(string.Join("; ", source.Genres)),
                Year = source.Year == 0 ? null : source.Year,
                TrackNumber = source.Track == 0 ? null : source.Track,
                TrackCount = source.TrackCount == 0 ? null : source.TrackCount,
                DiscNumber = source.Disc == 0 ? null : source.Disc,
                DiscCount = source.DiscCount == 0 ? null : source.DiscCount,
                CoverImageData = coverData,
                CoverMimeType = coverMime
            };
        }

        /// <summary>
        /// Schreibt alle nicht-null-Felder aus <paramref name="source"/> in das TagLib-Tag-Objekt.
        /// Felder mit dem Wert <see langword="null"/> werden bewusst übersprungen,
        /// damit bestehende Werte erhalten bleiben (partielles Update).
        /// </summary>
        private static void ApplyAudioTag(TagLib.Tag target, AudioTag source)
        {
            if (source.Title is not null)
            {
                target.Title = source.Title;
            }

            if (source.Album is not null)
            {
                target.Album = source.Album;
            }

            if (source.Artist is not null)
            {
                // TagLib erwartet ein Array; wir übergeben den Künstler als einelementiges Array
                target.Performers = [source.Artist];
            }

            if (source.AlbumArtist is not null)
            {
                target.AlbumArtists = [source.AlbumArtist];
            }

            if (source.Comment is not null)
            {
                target.Comment = source.Comment;
            }

            if (source.Genre is not null)
            {
                target.Genres = [source.Genre];
            }

            if (source.Year.HasValue)
            {
                target.Year = source.Year.Value;
            }

            if (source.TrackNumber.HasValue)
            {
                target.Track = source.TrackNumber.Value;
            }

            if (source.TrackCount.HasValue)
            {
                target.TrackCount = source.TrackCount.Value;
            }

            if (source.DiscNumber.HasValue)
            {
                target.Disc = source.DiscNumber.Value;
            }

            if (source.DiscCount.HasValue)
            {
                target.DiscCount = source.DiscCount.Value;
            }

            if (source.CoverImageData is not null)
            {
                TagLib.Picture picture = new()
                {
                    Data = new TagLib.ByteVector(source.CoverImageData),
                    MimeType = source.CoverMimeType ?? "image/jpeg",
                    Type = TagLib.PictureType.FrontCover
                };
                target.Pictures = [picture];
            }
        }

        /// <summary>
        /// Gibt <see langword="null"/> zurück, wenn <paramref name="value"/> leer oder
        /// nur aus Leerzeichen besteht, andernfalls den unveränderten Wert.
        /// Verhindert, dass leere Strings als "gesetztes Feld" behandelt werden.
        /// </summary>
        private static string? NullIfEmpty(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
