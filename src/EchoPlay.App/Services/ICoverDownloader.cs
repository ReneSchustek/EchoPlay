using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt Cover-Bilddaten von einer Provider-URL. Einzige Stelle in der App-Schicht, die
    /// den HTTP-Abruf eines Covers ausführt — vier Aufrufer hatten das vorher je selbst
    /// implementiert, mit drei unterschiedlichen Fehlerverhalten.
    /// </summary>
    public interface ICoverDownloader
    {
        /// <summary>
        /// Lädt die Bilddaten hinter <paramref name="url"/>.
        /// </summary>
        /// <param name="url">Absolute Cover-URL. Leer oder nicht absolut ergibt <see langword="null"/>.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>
        /// Die Bilddaten, oder <see langword="null"/> bei HTTP-, TLS-, Redirect- oder
        /// Timeout-Fehlern sowie bei leerer Antwort. Der Aufrufer überspringt den Eintrag
        /// und macht mit dem nächsten weiter.
        /// </returns>
        /// <exception cref="System.OperationCanceledException">
        /// Wenn <paramref name="cancellationToken"/> den Abbruch angefordert hat. Ein
        /// abgebrochener Lauf ist kein fehlgeschlagener Download und wird nicht zu
        /// <see langword="null"/> verschluckt.
        /// </exception>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Cover-URLs stammen aus den DTOs der Provider-APIs und werden in der gesamten Cover-Pipeline als string geführt; eine Uri-Signatur würde nur an dieser Grenze konvertieren.")]
        Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default);
    }
}
