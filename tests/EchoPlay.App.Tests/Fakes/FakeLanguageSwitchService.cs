using EchoPlay.App.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILanguageSwitchService"/>.
    /// Hält die angefragten Sprachcodes fest, statt die Anwendung neu zu starten.
    /// </summary>
    internal sealed class FakeLanguageSwitchService : ILanguageSwitchService
    {
        /// <summary>Alle über <see cref="ChangeLanguageAsync"/> angefragten Sprachcodes.</summary>
        public List<string> ChangedLanguages { get; } = [];

        /// <summary>Alle über <see cref="ApplyOverride"/> gesetzten Sprachcodes.</summary>
        public List<string> AppliedOverrides { get; } = [];

        /// <summary>Antwort von <see cref="ChangeLanguageAsync"/>.</summary>
        public bool ChangeResult { get; set; } = true;

        /// <inheritdoc/>
        public bool ApplyOverride(string languageCode)
        {
            AppliedOverrides.Add(languageCode);
            return true;
        }

        /// <inheritdoc/>
        public Task<bool> ChangeLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
        {
            ChangedLanguages.Add(languageCode);
            return Task.FromResult(ChangeResult);
        }
    }
}
