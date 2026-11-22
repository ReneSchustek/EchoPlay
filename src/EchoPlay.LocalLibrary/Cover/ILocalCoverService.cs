namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Ermittelt das Coverbild einer Hörspielserie und speichert es dauerhaft im Serienordner.
    /// Durch die Ablage als <c>cover.jpg</c> auf der Festplatte bleibt das Cover auch nach einer
    /// Datenbank-Neu-Initialisierung erhalten, es muss dann nicht erneut online gesucht werden.
    /// </summary>
    public interface ILocalCoverService
    {
        /// <summary>
        /// Sucht ein Coverbild für eine Serie in dieser Reihenfolge:
        /// <list type="number">
        ///   <item>ein Front-Cover im Unterordner <c>Cover</c> des Serienordners</item>
        ///   <item><c>cover.jpg</c>, <c>folder.jpg</c>, <c>album.jpg</c> oder ein Vorschaubild des
        ///   Windows Media Players im Serienordner</item>
        ///   <item>der Download von <paramref name="coverImageUrl"/>, falls angegeben</item>
        /// </list>
        /// Die ID3-Bilder der Audiodateien bleiben außen vor; sie zeigen die Folge, nicht die Serie.
        /// Nur ein heruntergeladenes Cover wird als <c>cover.jpg</c> im Serienordner abgelegt, und
        /// nur, wenn dort noch keine liegt.
        /// </summary>
        /// <param name="seriesFolder">Absoluter Pfad zum Serienordner auf der Festplatte.</param>
        /// <param name="coverImageUrl">
        /// Optionale, geprüfte Adresse für den Online-Rückfall, etwa das Spotify-Coverbild.
        /// <see langword="null"/> lässt den Download aus.
        /// </param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>
        /// Rohe Bilddaten oder <see langword="null"/>, wenn kein Cover gefunden wurde oder der
        /// Download scheiterte; ein Fehlschlag steht als Warnung im Protokoll.
        /// </returns>
        Task<byte[]?> ResolveAsync(string seriesFolder,
            Uri? coverImageUrl,
            System.Threading.CancellationToken cancellationToken = default);
    }
}
