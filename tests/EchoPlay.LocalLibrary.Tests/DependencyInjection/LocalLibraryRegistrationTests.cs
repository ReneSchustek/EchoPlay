using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.DependencyInjection;
using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.DependencyInjection
{
    /// <summary>
    /// Prüft, dass die Verdrahtung der lokalen Mediathek jeden Dienst auflösbar macht,
    /// und die Mustererkennung auf einem nicht lesbaren Ordner.
    /// </summary>
    /// <remarks>
    /// Fehlt eine Registrierung, fällt das erst beim Öffnen der betroffenen Seite auf —
    /// zur Laufzeit, beim Anwender. Die Cover-Suche fasst fünf Anbieter zusammen; fehlt
    /// einer davon, scheitert nicht die Suche, sondern der Aufbau des Dienstes.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class LocalLibraryRegistrationTests : IDisposable
    {
        private readonly string _wurzel;

        public LocalLibraryRegistrationTests()
        {
            _wurzel = Directory.CreateTempSubdirectory("echoplay_di_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_wurzel))
            {
                Directory.Delete(_wurzel, recursive: true);
            }
        }

        [Fact]
        public void AddLocalLibrary_MakesTheCoverSearchResolvable()
        {
            ServiceProvider anbieter = BuildProvider();

            ICoverSearchService suche = anbieter.GetRequiredService<ICoverSearchService>();

            // Der zusammengesetzte Dienst holt sich alle fünf Anbieter beim Auflösen.
            // Fehlt einer, wirft schon diese Zeile.
            Assert.NotNull(suche);
        }

        [Fact]
        public void AddLocalLibrary_MakesScannerAndReadersResolvable()
        {
            ServiceProvider anbieter = BuildProvider();

            Assert.NotNull(anbieter.GetRequiredService<ILocalLibraryScanner>());
            Assert.NotNull(anbieter.GetRequiredService<IScanOrchestrator>());
            Assert.NotNull(anbieter.GetRequiredService<ILocalCoverLoader>());
            Assert.NotNull(anbieter.GetRequiredService<ITagTitleReader>());
            Assert.NotNull(anbieter.GetRequiredService<IAudioMetadataReader>());
            Assert.NotNull(anbieter.GetRequiredService<CoverService>());
        }

        [Fact]
        public async Task PatternAnalysis_WithAnUnreadableSeriesFolder_KeepsGoing()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_wurzel, "Gesperrte Serie");

            string lesbar = Directory.CreateDirectory(Path.Combine(_wurzel, "TKKG")).FullName;
            _ = Directory.CreateDirectory(Path.Combine(lesbar, "001 - Der Fall"));
            _ = AudioTestFiles.CreateMp3(Path.Combine(lesbar, "001 - Der Fall"));

            EpisodePatternAnalyzer sut = new();

            IReadOnlyList<PatternSuggestion> suggestions = await sut.AnalyzeAsync(_wurzel);

            // Der Vorschlag für das Ordnermuster entsteht aus einer Stichprobe. Eine
            // gesperrte Serie darf die Stichprobe nicht beenden.
            Assert.NotNull(suggestions);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static ServiceProvider BuildProvider()
        {
            ServiceCollection dienste = new();
            _ = dienste.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
            _ = dienste.AddLocalLibrary();
            return dienste.BuildServiceProvider();
        }
    }
}
