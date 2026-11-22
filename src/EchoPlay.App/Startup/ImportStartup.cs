using EchoPlay.Core;
using EchoPlay.App.Services;
using EchoPlay.Data.Context;
using EchoPlay.Data.Replication;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Startup
{
    /// <summary>
    /// Der Teil des Starts, der ein vorgemerktes Einspielen ausführt: vor dem ersten Öffnen der
    /// Datenbank ersetzen, nach der Migration abschließen.
    /// </summary>
    /// <remarks>
    /// Eigene Klasse statt zweier Zeilen im Startlauf, damit sich der Ablauf mit einer gewöhnlichen
    /// Dienstsammlung prüfen lässt; der Startlauf selbst braucht Fenster, Protokollierung und Sprache.
    /// </remarks>
    internal static class ImportStartup
    {
        /// <summary>
        /// Ersetzt die Datenbank, wenn ein Einspielen ansteht. Muss laufen, bevor irgendetwas die
        /// Datenbank öffnet.
        /// </summary>
        /// <param name="scopeFactory">Für den Dateiort der Datenbank.</param>
        /// <returns>Was erhalten bleibt, oder <see langword="null"/>, wenn nichts anstand.</returns>
        public static async Task<ImportCarryOver?> BeforeOpenAsync(IServiceScopeFactory scopeFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            return await PendingImportApplier.ApplyAsync(DatabasePath(scopeFactory)).ConfigureAwait(false);
        }

        /// <summary>
        /// Schreibt nach der Migration das Gerätegebundene zurück, rechnet die Pfade um und übernimmt
        /// vorgemerkte Zugangsdaten.
        /// </summary>
        /// <param name="scopeFactory">Für die Dienste des Abschlusses.</param>
        /// <param name="carryOver">Was vor dem Ersetzen gelesen wurde.</param>
        /// <param name="logger">Protokolliert die Übernahme der Zugangsdaten.</param>
        /// <returns>Was der Abschluss getan hat.</returns>
        public static async Task<ImportFinished> AfterMigrationAsync(IServiceScopeFactory scopeFactory, ImportCarryOver carryOver, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(carryOver);
            ArgumentNullException.ThrowIfNull(logger);

            using IServiceScope scope = scopeFactory.CreateScope();
            ImportFinished finished = await scope.ServiceProvider.GetRequiredService<ImportFinisher>()
                .FinishAsync(carryOver).ConfigureAwait(false);

            ISpotifyCredentialStore store = scope.ServiceProvider.GetRequiredService<ISpotifyCredentialStore>();
            string directory = ParentFolder.Of(DatabasePath(scopeFactory));
            if (await DataTransferService.ApplyPendingCredentialsAsync(directory, store, CancellationToken.None).ConfigureAwait(false))
            {
                logger.Info("Zugangsdaten aus dem Paket übernommen.");
            }

            return finished;
        }

        private static string DatabasePath(IServiceScopeFactory scopeFactory)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<DatabaseFileLocation>().FilePath;
        }
    }
}
