using EchoPlay.App.Services;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IConfirmationDialogService"/>.
    /// Gibt immer das konfigurierte Ergebnis zurück, ohne einen WinUI-3-Dialog anzuzeigen.
    /// Standard ist <see langword="true"/> (Benutzer hat bestätigt).
    /// </summary>
    internal sealed class FakeConfirmationDialogService : IConfirmationDialogService
    {
        private readonly bool _result;

        /// <summary>
        /// Anzahl der Aufrufe von <see cref="ConfirmAsync"/>.
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// Initialisiert den Fake mit dem zu liefernden Ergebnis.
        /// </summary>
        /// <param name="result">
        /// <see langword="true"/> wenn der Fake „Ja" zurückgeben soll, <see langword="false"/> für „Abbrechen".
        /// Standard: <see langword="true"/>.
        /// </param>
        public FakeConfirmationDialogService(bool result = true)
        {
            _result = result;
        }

        /// <inheritdoc/>
        public Task<bool> ConfirmAsync(string title, string message)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
