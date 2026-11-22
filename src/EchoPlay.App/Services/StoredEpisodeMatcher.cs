using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Ordnet beim erneuten Einlesen die gefundenen Ordner den gespeicherten Folgen einer Serie zu.
    /// </summary>
    internal static class StoredEpisodeMatcher
    {
        /// <summary>
        /// Ordnet eingelesene Ordner den gespeicherten Folgen einer Serie zu: zuerst über den
        /// Ordnerpfad, danach über die Nummer. Jede gespeicherte Folge wird höchstens einmal
        /// vergeben.
        /// </summary>
        /// <remarks>
        /// Der Pfad muss zuerst kommen. Stand eine Nummer falsch in der Datenbank, etwa weil
        /// eine Sonderfolge die 87 trug, fände die Nummer allein die falsche Folge, und der
        /// echte Ordner 087 würde ihr zugeordnet. Über die Nummer werden nur Ordner zugeordnet,
        /// deren Pfad unbekannt ist (umbenannt oder neu), und nur an Folgen, die kein anderer
        /// Ordner schon beansprucht hat.
        ///
        /// Bei einer flachen Serie teilen sich alle Folgen den Serienordner als Pfad; der Pfad
        /// unterscheidet dort nichts, es zählt nur die Nummer.
        /// </remarks>
        /// <param name="scans">Die eingelesenen Ordner der Serie.</param>
        /// <param name="stored">Die gespeicherten Folgen derselben Serie.</param>
        /// <returns>Die Paare aus Ordner und Folge; Ordner ohne passende Folge fehlen.</returns>
        public static IReadOnlyList<(LocalEpisodeScan Scan, Episode Episode)> Match(
            IReadOnlyList<LocalEpisodeScan> scans,
            IReadOnlyList<Episode> stored)
        {
            Dictionary<string, Episode> storedByFolder = UniqueByFolder(stored, episode => episode.LocalFolderPath);
            HashSet<string> sharedScanFolders = SharedFolders(scans);

            HashSet<Episode> claimed = new(ReferenceEqualityComparer.Instance);
            List<(LocalEpisodeScan Scan, Episode Episode)> matches = [];
            List<LocalEpisodeScan> withoutFolderMatch = [];

            foreach (LocalEpisodeScan scan in scans)
            {
                if (!sharedScanFolders.Contains(scan.FolderPath)
                    && storedByFolder.TryGetValue(scan.FolderPath, out Episode? byFolder)
                    && claimed.Add(byFolder))
                {
                    matches.Add((scan, byFolder));
                }
                else
                {
                    withoutFolderMatch.Add(scan);
                }
            }

            MatchByNumber(withoutFolderMatch, stored, claimed, matches);
            return matches;
        }

        /// <summary>
        /// Zweite Stufe: Ordner ohne Pfadtreffer über die Folgennummer zuordnen; jede gespeicherte
        /// Folge wird höchstens einmal vergeben.
        /// </summary>
        private static void MatchByNumber(
            List<LocalEpisodeScan> scans,
            IReadOnlyList<Episode> stored,
            HashSet<Episode> claimed,
            List<(LocalEpisodeScan Scan, Episode Episode)> matches)
        {
            foreach (LocalEpisodeScan scan in scans)
            {
                if (scan.ParsedNumber is not int number)
                {
                    continue;
                }

                Episode? byNumber = stored.FirstOrDefault(episode => episode.EpisodeNumber == number && !claimed.Contains(episode));
                if (byNumber is not null && claimed.Add(byNumber))
                {
                    matches.Add((scan, byNumber));
                }
            }
        }

        /// <summary>
        /// Die eingelesenen Ordner, die <see cref="Match"/> keiner gespeicherten Folge zuordnen
        /// konnte. Sie sind neu und werden angelegt.
        /// </summary>
        /// <remarks>
        /// Ausgenommen sind Dateien einer flachen Serie ohne erkennbare Nummer. Sie teilen sich
        /// den Serienordner als Pfad und ließen sich beim nächsten Einlesen weder über den Pfad
        /// noch über die Nummer wiederfinden; jedes Einlesen legte sie sonst erneut an.
        /// </remarks>
        /// <param name="scans">Die eingelesenen Ordner der Serie.</param>
        /// <param name="matches">Das Ergebnis von <see cref="Match"/> für dieselben Ordner.</param>
        /// <returns>Die Ordner, für die eine neue Folge anzulegen ist.</returns>
        public static IReadOnlyList<LocalEpisodeScan> Unmatched(
            IReadOnlyList<LocalEpisodeScan> scans,
            IReadOnlyList<(LocalEpisodeScan Scan, Episode Episode)> matches)
        {
            ArgumentNullException.ThrowIfNull(scans);
            ArgumentNullException.ThrowIfNull(matches);

            HashSet<LocalEpisodeScan> matched = new(matches.Select(match => match.Scan), ReferenceEqualityComparer.Instance);
            HashSet<string> sharedFolders = SharedFolders(scans);

            return scans.Where(scan =>
                !matched.Contains(scan)
                && (scan.ParsedNumber is not null || !sharedFolders.Contains(scan.FolderPath))).ToArray();
        }

        /// <summary>
        /// Ob eine gespeicherte Folge schon genau dem eingelesenen Ordner entspricht: gleiche
        /// Nummer, gleicher Ordner und dieselben Dateien. Dann gibt es nichts nachzuziehen.
        /// </summary>
        /// <remarks>
        /// Eine ausgetauschte Datei unter gleichem Namen fällt dabei nicht auf; ihre Dauer
        /// bleibt die alte, bis „Neu-Initialisierung" alles neu einliest.
        /// </remarks>
        /// <param name="episode">Die gespeicherte Folge.</param>
        /// <param name="scan">Der zugeordnete Ordner.</param>
        /// <param name="storedTracks">Die gespeicherten Spuren der Folge.</param>
        /// <returns><see langword="true"/>, wenn nichts zu aktualisieren ist.</returns>
        public static bool IsUnchanged(Episode episode, LocalEpisodeScan scan, IReadOnlyList<LocalTrack> storedTracks)
        {
            ArgumentNullException.ThrowIfNull(episode);
            ArgumentNullException.ThrowIfNull(scan);
            ArgumentNullException.ThrowIfNull(storedTracks);

            return episode.EpisodeNumber == scan.ParsedNumber
                && string.Equals(episode.LocalFolderPath, scan.FolderPath, StringComparison.OrdinalIgnoreCase)
                && episode.LocalTrackCount == scan.TrackCount
                && storedTracks.Count == scan.TrackCount
                && storedTracks.Select(track => track.FilePath).Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(scan.TrackPaths.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Pfade, die mehrere eingelesene Folgen teilen: der Serienordner einer flachen Serie.
        /// </summary>
        private static HashSet<string> SharedFolders(IReadOnlyList<LocalEpisodeScan> scans)
        {
            return new HashSet<string>(
                scans.GroupBy(scan => scan.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Folgen, deren Ordner genau einmal vorkommt, nach Ordnerpfad. Ein Pfad, den mehrere
        /// Folgen teilen (flache Serie), taugt nicht zur Zuordnung und fehlt.
        /// </summary>
        private static Dictionary<string, Episode> UniqueByFolder(IReadOnlyList<Episode> stored, Func<Episode, string?> folderOf)
        {
            return stored
                .Where(episode => !string.IsNullOrEmpty(folderOf(episode)))
                .GroupBy(episode => folderOf(episode)!, StringComparer.OrdinalIgnoreCase) // nach dem Filter oben nie null
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
