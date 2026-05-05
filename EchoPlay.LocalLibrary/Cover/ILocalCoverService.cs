using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Ermittelt das Coverbild einer Hörspielserie und speichert es dauerhaft im Serienordner.
    /// Durch die Ablage als <c>cover.jpg</c> auf der Festplatte bleibt das Cover auch nach einer
    /// Datenbank-Neu-Initialisierung erhalten – es muss dann nicht erneut online gesucht werden.
    /// </summary>
    public interface ILocalCoverService
    {
        /// <summary>
        /// Sucht ein Coverbild für eine Serie nach festgelegter Priorität:
        /// <list type="number">
        ///   <item><c>cover.jpg</c>, <c>folder.jpg</c> oder <c>album.jpg</c> im Serienordner</item>
        ///   <item>ID3-Cover aus der ersten MP3-Datei im Serienordner</item>
        ///   <item>Online-Download über die <see cref="CoverService"/>-URL (falls vorhanden)</item>
        /// </list>
        /// Gefundene Cover werden als <c>cover.jpg</c> im Serienordner gespeichert,
        /// sofern diese Datei noch nicht existiert.
        /// </summary>
        /// <param name="seriesFolder">Absoluter Pfad zum Serienordner auf der Festplatte.</param>
        /// <param name="coverImageUrl">
        /// Optionale URL für den Online-Fallback (z.B. Spotify-Coverbild-URL).
        /// <see langword="null"/> deaktiviert den Download-Schritt.
        /// </param>
        /// <returns>
        /// Rohe Bilddaten als Byte-Array oder <see langword="null"/> wenn kein Cover gefunden wurde.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<byte[]?> ResolveAsync(string seriesFolder,
            [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
                Justification = "Internal API nimmt die URL so entgegen, wie sie in der DB-Spalte Series.CoverImageUrl abgelegt ist. Uri-Refactor würde Cascade durch Cover-Kaskade erfordern und ist bewusst nicht umgesetzt.")]
            string? coverImageUrl,
            System.Threading.CancellationToken cancellationToken = default);
    }
}
