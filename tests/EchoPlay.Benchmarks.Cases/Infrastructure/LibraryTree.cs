namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Legt einen Ordnerbaum wie eine lokale Hörspiel-Mediathek an: Serienordner, darin Folgenordner
    /// nach dem Muster „001 - Titel“, darin leere MP3-Dateien. Gemessen wird das Durchsuchen der
    /// Ordner, nicht das Lesen von Tags; dafür genügen leere Dateien.
    /// </summary>
    internal sealed class LibraryTree : IDisposable
    {
        /// <summary>Das Muster, nach dem die Folgenordner benannt sind.</summary>
        public const string FolderPattern = "{number:000} - {title}";

        private LibraryTree(string root, int fileCount)
        {
            Root = root;
            FileCount = fileCount;
        }

        /// <summary>Wurzel des Baums im Temp-Verzeichnis.</summary>
        public string Root { get; }

        /// <summary>Anzahl der angelegten Audiodateien, zur Prüfung, dass die Messung über sie lief.</summary>
        public int FileCount { get; }

        /// <summary>Legt den Baum an.</summary>
        /// <param name="series">Anzahl der Serienordner.</param>
        /// <param name="episodesPerSeries">Folgenordner je Serie.</param>
        /// <param name="tracksPerEpisode">Audiodateien je Folge.</param>
        /// <returns>Der Baum; <see cref="Dispose"/> löscht ihn wieder.</returns>
        public static LibraryTree Create(int series, int episodesPerSeries, int tracksPerEpisode)
        {
            string root = Directory.CreateTempSubdirectory("echoplay_bench_").FullName;
            for (int s = 0; s < series; s++)
            {
                string seriesFolder = Path.Combine(root, $"Messserie {s:000}");
                for (int e = 1; e <= episodesPerSeries; e++)
                {
                    string episodeFolder = Path.Combine(seriesFolder, $"{e:000} - Messfolge {e}");
                    _ = Directory.CreateDirectory(episodeFolder);
                    for (int t = 1; t <= tracksPerEpisode; t++)
                    {
                        File.WriteAllBytes(Path.Combine(episodeFolder, $"{t:00}.mp3"), []);
                    }
                }
            }

            return new LibraryTree(root, series * episodesPerSeries * tracksPerEpisode);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
