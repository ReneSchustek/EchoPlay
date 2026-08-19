using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, wann die Wiedergabeseite die laufende Liste übernimmt.
    /// </summary>
    /// <remarks>
    /// Die Wiedergabe startet fast immer woanders — in der Mediathek, der Serienansicht
    /// oder über „Weiterhören". Ohne den Abgleich zeigt die Seite den Platzhalter,
    /// während unten die Folge läuft. Er darf aber auch nicht bei jedem Positions-Tick
    /// zuschlagen: Ein Neuaufbau der Liste alle 500 ms löscht die gerade nachgetragenen
    /// Titel wieder.
    /// </remarks>
    public sealed class PlayerPlaylistAdoptionTests
    {
        [Fact]
        public void RunningPlaylist_WhenItStaysTheSame_IsNotRebuilt()
        {
            FakePlayerService playerService = new();
            PlayerViewModel sut = BuildViewModel(playerService);

            playerService.SimulateExternalPlayback([@"D:\Media\01.mp3", @"D:\Media\02.mp3"], @"D:\Media\01.mp3");
            IReadOnlyList<PlaylistItemViewModel> first = [.. sut.PlaylistItems];

            playerService.SimulateExternalPlayback([@"D:\Media\01.mp3", @"D:\Media\02.mp3"], @"D:\Media\02.mp3");

            // Dieselben Pfade in derselben Reihenfolge: Die Einträge bleiben dieselben
            // Objekte, statt neu erzeugt zu werden.
            Assert.Equal(first, sut.PlaylistItems);
        }

        [Fact]
        public void RunningPlaylist_WhenATrackDiffers_IsTakenOver()
        {
            FakePlayerService playerService = new();
            PlayerViewModel sut = BuildViewModel(playerService);

            playerService.SimulateExternalPlayback([@"D:\Media\01.mp3", @"D:\Media\02.mp3"], @"D:\Media\01.mp3");

            // Gleiche Anzahl, andere Dateien — der Anwender hat eine andere Folge gestartet.
            // Ein Vergleich nur über die Anzahl würde das übersehen.
            playerService.SimulateExternalPlayback([@"D:\Media\01.mp3", @"D:\Media\09.mp3"], @"D:\Media\09.mp3");

            Assert.Equal(
                [@"D:\Media\01.mp3", @"D:\Media\09.mp3"],
                sut.PlaylistItems.Select(i => i.FullPath));
        }

        [Fact]
        public void RunningPlaylist_WithACoverInTheTrack_LeavesTheViewIntact()
        {
            FakePlayerService playerService = new();
            PlayerViewModel sut = BuildViewModel(playerService, new FixedCoverLoader());

            playerService.SimulateExternalPlayback([@"D:\Media\01.mp3"], @"D:\Media\01.mp3");

            // Das Bild selbst entsteht erst im Fenster. Ohne Fenster bleibt der Platz leer —
            // die Seite muss trotzdem stehen und die Liste zeigen.
            _ = Assert.Single(sut.PlaylistItems);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static PlayerViewModel BuildViewModel(
            FakePlayerService playerService, ILocalCoverLoader? coverLoader = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());

            if (coverLoader is not null)
            {
                _ = services.AddScoped(_ => coverLoader);
            }

            ServiceProvider provider = services.BuildServiceProvider();

            return new PlayerViewModel(playerService, provider.GetRequiredService<IServiceScopeFactory>());
        }

        /// <summary>Cover-Lader, der für jede Spur dieselben Bilddaten liefert.</summary>
        private sealed class FixedCoverLoader : ILocalCoverLoader
        {
            public System.Threading.Tasks.Task<byte[]?> LoadAsync(
                string? episodeFolderPath, string? firstTrackPath)
                => System.Threading.Tasks.Task.FromResult<byte[]?>([0x01, 0x02, 0x03]);
        }
    }
}
