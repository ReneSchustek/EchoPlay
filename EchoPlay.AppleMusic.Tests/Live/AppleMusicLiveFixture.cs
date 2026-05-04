using System.Diagnostics.CodeAnalysis;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Clients;

namespace EchoPlay.AppleMusic.Tests.Live
{
    /// <summary>
    /// Gemeinsame Test-Infrastruktur für alle Apple-Music-Live-Smoke-Tests.
    /// Die Fixture erstellt einen echten iTunes-Search-Client.
    /// Im Gegensatz zu Spotify ist keine Authentifizierung erforderlich,
    /// da die iTunes Search API kostenfrei und öffentlich zugänglich ist.
    /// </summary>
    [SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit-Test-Infrastruktur muss public sein für den Test-Runner (IClassFixture<T>).")]
    public sealed class AppleMusicLiveFixture : IDisposable
    {
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Der vorkonfigurierte iTunes-Search-Client.
        /// </summary>
        public IAppleMusicSearchClient SearchClient { get; }

        /// <summary>
        /// Erstellt die Fixture mit einem echten HTTP-Client gegen die iTunes Search API.
        /// </summary>
        public AppleMusicLiveFixture()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://itunes.apple.com/")
            };

            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory =
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

            SearchClient = new AppleMusicSearchClient(_httpClient, loggerFactory);
        }

        /// <summary>
        /// Gibt die HTTP-Ressourcen frei.
        /// </summary>
        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
