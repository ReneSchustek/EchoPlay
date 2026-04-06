using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IAppSettingsDataService"/>.
    /// Gibt eine fest konfigurierte <see cref="AppSettings"/>-Instanz zurück.
    /// </summary>
    internal sealed class FakeAppSettingsDataService : IAppSettingsDataService
    {
        private AppSettings _settings;

        /// <summary>
        /// Erstellt den Fake mit optionaler Startkonfiguration.
        /// </summary>
        /// <param name="settings">Die zurückzugebenden Einstellungen. Standard: AppSettings mit Standardwerten.</param>
        public FakeAppSettingsDataService(AppSettings? settings = null)
        {
            _settings = settings ?? new AppSettings();
        }

        /// <summary>Gibt an, wie oft <see cref="SaveAsync"/> aufgerufen wurde.</summary>
        public int SaveCallCount { get; private set; }

        /// <inheritdoc/>
        public Task<AppSettings> GetAsync()
        {
            return Task.FromResult(_settings);
        }

        /// <inheritdoc/>
        public Task SaveAsync(AppSettings settings)
        {
            _settings = settings;
            SaveCallCount++;
            return Task.CompletedTask;
        }
    }
}
