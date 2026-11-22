using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, wann ein übernommenes Cover zusätzlich als <c>cover.jpg</c> im Ordner landet.
    /// </summary>
    /// <remarks>
    /// Die Datei ist die Brücke zu allen anderen Programmen, die den Ordner lesen. Sie darf
    /// aber nur entstehen, wenn der Anwender das eingestellt hat, in einer Sammlung, die
    /// woanders gepflegt wird, ist eine fremde Datei im Ordner eine Zumutung.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis und räumen sie wieder ab;
    /// der geprüfte Weg schreibt eine Datei und lässt sich anders nicht festhalten.
    /// </remarks>
    public sealed class EpisodeCoverDirectoryTests
    {
        private static readonly byte[] CoverBytes = [0x10, 0x20, 0x30];

        [Fact]
        public async Task ApplyEpisodeCoverFromBytes_WithTheOptionEnabled_WritesCoverJpgIntoTheFolder()
        {
            string folder = CreateTempFolder();
            try
            {
                EpisodeCoverCoordinator sut = BuildCoordinator(saveCoverToDirectory: true);
                LocalEpisodeCardViewModel card = BuildEpisodeCard(folder);

                await sut.ApplyEpisodeCoverFromBytesAsync(card, CoverBytes, TestContext.Current.CancellationToken);

                string coverPath = Path.Combine(folder, EchoPlay.Core.CoverConstants.CoverFileName);
                Assert.True(File.Exists(coverPath));
                Assert.Equal(CoverBytes, await File.ReadAllBytesAsync(coverPath, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task ApplyEpisodeCoverFromBytes_WithTheOptionDisabled_LeavesTheFolderUntouched()
        {
            string folder = CreateTempFolder();
            try
            {
                EpisodeCoverCoordinator sut = BuildCoordinator(saveCoverToDirectory: false);
                LocalEpisodeCardViewModel card = BuildEpisodeCard(folder);

                await sut.ApplyEpisodeCoverFromBytesAsync(card, CoverBytes, TestContext.Current.CancellationToken);

                Assert.Empty(Directory.GetFiles(folder));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task ApplyEpisodeCoverFromBytes_WhenTheFolderDoesNotExist_WritesNoFile()
        {
            string folder = Path.Combine(Path.GetTempPath(), "echoplay-cover-gibt-es-nicht");
            EpisodeCoverCoordinator sut = BuildCoordinator(saveCoverToDirectory: true);
            LocalEpisodeCardViewModel card = BuildEpisodeCard(folder);

            await sut.ApplyEpisodeCoverFromBytesAsync(card, CoverBytes, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(folder));
        }

        [Fact]
        public async Task ApplySeriesCoverFromBytes_WithTheOptionEnabled_WritesCoverJpgIntoTheSeriesFolder()
        {
            string folder = CreateTempFolder();
            try
            {
                EpisodeCoverCoordinator sut = BuildCoordinator(saveCoverToDirectory: true);
                LocalArtistCardViewModel card = BuildArtistCard(localFolderPath: folder);

                await sut.ApplySeriesCoverFromBytesAsync(card, CoverBytes, TestContext.Current.CancellationToken);

                Assert.True(File.Exists(Path.Combine(folder, EchoPlay.Core.CoverConstants.CoverFileName)));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task ApplyEpisodeCoverFromBytes_WithExistingCoverFile_OverwritesIt()
        {
            string folder = CreateTempFolder();
            try
            {
                string coverPath = Path.Combine(folder, EchoPlay.Core.CoverConstants.CoverFileName);
                await File.WriteAllBytesAsync(coverPath, [0x99], TestContext.Current.CancellationToken);

                EpisodeCoverCoordinator sut = BuildCoordinator(saveCoverToDirectory: true);
                LocalEpisodeCardViewModel card = BuildEpisodeCard(folder);

                await sut.ApplyEpisodeCoverFromBytesAsync(card, CoverBytes, TestContext.Current.CancellationToken);

                // Der Anwender hat ein neues Cover gewählt; bliebe das alte liegen, zeigten
                // andere Programme weiter das falsche Bild.
                Assert.Equal(CoverBytes, await File.ReadAllBytesAsync(coverPath, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task ApplySelectedSeriesCover_WhenTheDownloadFails_ShowsAMessage()
        {
            FakeErrorDialogService errorDialog = new();
            EpisodeCoverCoordinator sut = BuildCoordinator(
                saveCoverToDirectory: true, errorDialog: errorDialog);
            CoverSearchHit hit = new(
                new Uri("https://example.invalid/thumb.jpg"), new Uri("https://example.invalid/full.jpg"),
                "TKKG", "Cover Art Archive");

            await sut.ApplySelectedSeriesCoverAsync(
                BuildArtistCard(localFolderPath: null), hit, TestContext.Current.CancellationToken);

            // Ohne Meldung sieht der Anwender nur, dass sein Klick auf den Treffer nichts
            // bewirkt, und sucht den Fehler bei sich.
            _ = Assert.Single(errorDialog.ShownDialogs);
        }

        [Fact]
        public async Task ApplySelectedEpisodeCover_WhenTheDownloadFails_ShowsAMessage()
        {
            FakeErrorDialogService errorDialog = new();
            EpisodeCoverCoordinator sut = BuildCoordinator(
                saveCoverToDirectory: true, errorDialog: errorDialog);
            CoverSearchHit hit = new(
                new Uri("https://example.invalid/thumb.jpg"), new Uri("https://example.invalid/full.jpg"),
                "Folge 1", "Cover Art Archive");

            await sut.ApplySelectedEpisodeCoverAsync(
                BuildEpisodeCard(folderPath: null!), hit, TestContext.Current.CancellationToken);

            _ = Assert.Single(errorDialog.ShownDialogs);
        }

        [Fact]
        public async Task SearchCovers_HandsQueryAndPageToTheSearchService()
        {
            FakeCoverSearchService search = new();
            EpisodeCoverCoordinator sut = BuildCoordinator(saveCoverToDirectory: false, search: search);

            _ = await sut.SearchCoversAsync(
                "TKKG", EchoPlay.LocalLibrary.Cover.CoverSearchPage.First,
                TestContext.Current.CancellationToken);

            Assert.Equal("TKKG", search.LastSearchTitle);
        }

        private static EpisodeCoverCoordinator BuildCoordinator(
            bool saveCoverToDirectory,
            FakeErrorDialogService? errorDialog = null,
            FakeCoverSearchService? search = null)
        {
            FakeAppSettingsDataService settings = new(
                new AppSettings { SaveCoverToDirectory = saveCoverToDirectory });

            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settings);
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new EpisodeCoverCoordinator(
                scopeFactory,
                search ?? new FakeCoverSearchService(),
                new FakeCoverService(),
                new FakeConfirmationDialogService(),
                errorDialog ?? new FakeErrorDialogService(),
                new FakeCoverDownloader(),
                new FakeLocalizationService());
        }

        private static LocalEpisodeCardViewModel BuildEpisodeCard(string? folderPath)
            => new(
                new LocalEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeA,
                    EpisodeNumber = 1,
                    Title = "Folge 1",
                    LocalTrackCount = 1,
                    FolderPath = folderPath,
                },
                coverImage: null);

        private static LocalArtistCardViewModel BuildArtistCard(string? localFolderPath)
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistCardViewModel(
                new LocalArtistCardData
                {
                    SeriesId = TestIds.SeriesA,
                    Title = "Die drei Fragezeichen",
                    LocalFolderPath = localFolderPath,
                    LocalEpisodeCount = 3,
                    TotalEpisodeCount = 3,
                },
                coverImage: null,
                scopeFactory: provider.GetRequiredService<IServiceScopeFactory>());
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-coverdir-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}
