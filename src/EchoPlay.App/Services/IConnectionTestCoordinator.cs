using EchoPlay.Data.Entities.Settings;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Ergebnis eines Verbindungstests gegen einen Metadaten-Anbieter.
    /// <paramref name="ErrorDetail"/> ist bei Erfolg <see langword="null"/> und bei einem
    /// Fehlschlag die rohe Fehlermeldung des Providers (ohne Lokalisierungspräfix).
    /// </summary>
    /// <param name="Success">Ob der Test erfolgreich war.</param>
    /// <param name="ErrorDetail">Rohe Fehlermeldung im Fehlerfall, sonst <see langword="null"/>.</param>
    public sealed record ConnectionTestResult(bool Success, string? ErrorDetail);

    /// <summary>
    /// App-Service, der eine Minimal-Abfrage gegen den aktiven Metadaten-Provider
    /// (Spotify oder Apple Music) absetzt, um Token-Fluss und Netzwerk zu prüfen.
    /// Lokalisierung der Erfolgs-/Fehlermeldung bleibt bewusst außerhalb, damit der Coordinator
    /// frei von WinUI-Resource-Abhängigkeiten testbar ist.
    /// </summary>
    public interface IConnectionTestCoordinator
    {
        /// <summary>
        /// Führt einen Verbindungstest gegen den angegebenen Provider aus.
        /// Für <see cref="ProviderType.None"/> wird ein Misserfolg zurückgegeben, ohne API-Aufruf.
        /// </summary>
        /// <param name="provider">Der zu testende Metadaten-Provider.</param>
        /// <param name="cancellationToken">Token zum vorzeitigen Abbruch.</param>
        /// <returns>Das Testergebnis mit optionalem Fehlerdetail.</returns>
        Task<ConnectionTestResult> TestAsync(ProviderType provider, CancellationToken cancellationToken = default);
    }
}
