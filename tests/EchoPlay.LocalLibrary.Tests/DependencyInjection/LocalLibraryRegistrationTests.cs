using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.DependencyInjection;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.LocalLibrary.Tests.DependencyInjection
{
    /// <summary>
    /// Prüft, dass die Verdrahtung der lokalen Mediathek jeden Dienst auflösbar macht,
    /// und die Mustererkennung auf einem nicht lesbaren Ordner.
    /// </summary>
    /// <remarks>
    /// Fehlt eine Registrierung, fällt das erst beim Öffnen der betroffenen Seite auf,
    /// zur Laufzeit, beim Anwender. Die Cover-Suche fasst fünf Anbieter zusammen; fehlt
    /// einer davon, scheitert nicht die Suche, sondern der Aufbau des Dienstes.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class LocalLibraryRegistrationTests : IDisposable
    {
        private readonly string _root;

        public LocalLibraryRegistrationTests()
        {
            _root = Directory.CreateTempSubdirectory("echoplay_di_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public void AddLocalLibrary_MakesTheCoverSearchResolvable()
        {
            ServiceProvider provider = BuildProvider();

            ICoverSearchService search = provider.GetRequiredService<ICoverSearchService>();

            // Der zusammengesetzte Dienst holt sich alle fünf Anbieter beim Auflösen.
            // Fehlt einer, wirft schon diese Zeile.
            Assert.NotNull(search);
        }

        [Fact]
        public void AddLocalLibrary_MakesScannerAndReadersResolvable()
        {
            ServiceProvider provider = BuildProvider();

            Assert.NotNull(provider.GetRequiredService<ILocalLibraryScanner>());
            Assert.NotNull(provider.GetRequiredService<IScanOrchestrator>());
            Assert.NotNull(provider.GetRequiredService<ILocalCoverLoader>());
            Assert.NotNull(provider.GetRequiredService<ITagTitleReader>());
            Assert.NotNull(provider.GetRequiredService<IAudioMetadataReader>());
            Assert.NotNull(provider.GetRequiredService<CoverService>());
        }

        [Fact]
        public async Task PatternAnalysis_WithAnUnreadableSeriesFolder_KeepsGoing()
        {
            using LockedFolder locked = LockedFolder.Create(_root, "Gesperrte Serie");

            string lesbar = Directory.CreateDirectory(Path.Combine(_root, "TKKG")).FullName;
            _ = Directory.CreateDirectory(Path.Combine(lesbar, "001 - Der Fall"));
            _ = AudioTestFiles.CreateMp3(Path.Combine(lesbar, "001 - Der Fall"));

            EpisodePatternAnalyzer sut = new();

            IReadOnlyList<PatternSuggestion> suggestions = await sut.AnalyzeAsync(_root);

            // Der Vorschlag für das Ordnermuster entsteht aus einer Stichprobe. Eine
            // gesperrte Serie darf die Stichprobe nicht beenden.
            Assert.NotNull(suggestions);
        }

        private static ServiceProvider BuildProvider()
        {
            ServiceCollection services = new();
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddLocalLibrary();
            return services.BuildServiceProvider();
        }
    }
}
