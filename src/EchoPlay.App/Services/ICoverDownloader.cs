namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt Cover-Bilddaten von einer Anbieter-Adresse. Einzige Stelle in der App-Schicht, die
    /// den HTTP-Abruf eines Covers ausführt, damit alle Aufrufer dasselbe Fehlerverhalten sehen.
    /// </summary>
    public interface ICoverDownloader
    {
        /// <summary>
        /// Lädt die Bilddaten hinter <paramref name="url"/>.
        /// </summary>
        /// <param name="url">Die geprüfte Cover-Adresse (siehe <see cref="EchoPlay.Core.Http.WebAddress"/>).</param>
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
        Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken = default);
    }
}
