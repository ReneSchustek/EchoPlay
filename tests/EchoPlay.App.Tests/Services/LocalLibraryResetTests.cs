using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="LocalLibraryReset"/> — das Zurücksetzen vor einer Neu-Einlesung.
    /// </summary>
    /// <remarks>
    /// Der Vorgang ist am laufenden Programm nicht prüfbar: Er löst die Verbindung zwischen
    /// Datenbank und Festplatte für den gesamten Bestand. Dass er die richtige Grenze zieht —
    /// rein lokale Serien ganz weg, Anbieter-Serien nur ohne Zuordnung — steht deshalb hier.
    /// </remarks>
    public sealed class LocalLibraryResetTests
    {
        private static (LocalLibraryReset Reset,
                        FakeSeriesDataService Series,
                        FakeEpisodeDataService Episodes,
                        FakeLocalTrackDataService Tracks,
                        FakeCoverImageDataService Covers) Build()
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeLocalTrackDataService trackService = new();
            FakeCoverImageDataService coverImageService = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService);
            _ = services.AddScoped<ICoverImageDataService>(_ => coverImageService);

            ServiceProvider provider = services.BuildServiceProvider();

            LocalLibraryReset reset = new(provider.GetRequiredService<IServiceScopeFactory>());
            return (reset, seriesService, episodeService, trackService, coverImageService);
        }

        [Fact]
        public async Task ClearLocalAssignmentsAsync_LocalOnlySeries_IsRemoved()
        {
            // Eine Serie ohne Anbieter-Kennung besteht nur aus ihren Dateien. Bleibt sie
            // stehen, führt die Mediathek danach einen Eintrag ohne Inhalt.
            (LocalLibraryReset reset, FakeSeriesDataService series, _, _, _) = Build();

            Series localOnly = new() { Title = "Nur lokal", LocalFolderPath = @"D:\Mp3\Nur lokal" };
            await series.AddAsync(localOnly, TestContext.Current.CancellationToken);

            await reset.ClearLocalAssignmentsAsync();

            IReadOnlyList<Series> remaining = await series.GetAllAsync(TestContext.Current.CancellationToken);
            Assert.Empty(remaining);
        }

        [Fact]
        public async Task ClearLocalAssignmentsAsync_ProviderSeries_KeepsSeriesWithoutFolder()
        {
            // Eine vom Anbieter importierte Serie behält ihre Angaben — verloren geht nur
            // die Zuordnung zur Festplatte.
            (LocalLibraryReset reset, FakeSeriesDataService series, _, _, _) = Build();

            Series fromProvider = new()
            {
                Title = "Vom Anbieter",
                AppleMusicArtistId = "12345",
                LocalFolderPath = @"D:\Mp3\Vom Anbieter"
            };
            await series.AddAsync(fromProvider, TestContext.Current.CancellationToken);

            await reset.ClearLocalAssignmentsAsync();

            IReadOnlyList<Series> remaining = await series.GetAllAsync(TestContext.Current.CancellationToken);
            Series survivor = Assert.Single(remaining);
            Assert.Equal("Vom Anbieter", survivor.Title);
            Assert.Null(survivor.LocalFolderPath);
        }

        [Fact]
        public async Task ClearLocalAssignmentsAsync_Episodes_LoseFolderTrackCountAndMatch()
        {
            // Die Folge muss danach aussehen wie vor dem ersten Einlesen — sonst hielte die
            // Anzeige sie weiterhin für abgeglichen.
            (LocalLibraryReset reset,
             FakeSeriesDataService series,
             FakeEpisodeDataService episodes,
             FakeLocalTrackDataService tracks,
             _) = Build();

            Series fromProvider = new() { Title = "Vom Anbieter", SpotifyArtistId = "abc" };
            await series.AddAsync(fromProvider, TestContext.Current.CancellationToken);

            Episode episode = new()
            {
                Title = "Folge 1",
                SeriesId = fromProvider.Id,
                LocalFolderPath = @"D:\Mp3\Vom Anbieter\001",
                LocalTrackCount = 12,
                TrackMatchKind = TrackMatchKind.TbT
            };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            await reset.ClearLocalAssignmentsAsync();

            IReadOnlyList<Episode> afterReset = await episodes.GetBySeriesIdAsync(fromProvider.Id, TestContext.Current.CancellationToken);
            Episode updated = Assert.Single(afterReset);

            Assert.Null(updated.LocalFolderPath);
            Assert.Null(updated.LocalTrackCount);
            Assert.Equal(TrackMatchKind.NotMatched, updated.TrackMatchKind);

            // Eine leere Spurenliste ist das Löschen aller Spuren dieser Folge.
            Assert.True(tracks.SavedTracks.TryGetValue(episode.Id, out IReadOnlyList<LocalTrack>? saved));
            Assert.Empty(saved!);
        }

        [Fact]
        public async Task ClearLocalAssignmentsAsync_DeletesCoversOfBothLevels()
        {
            // Ohne dieses Zurücksetzen bliebe das alte Bild in der Datenbank stehen, selbst
            // wenn inzwischen ein anderes auf der Festplatte liegt.
            (LocalLibraryReset reset,
             FakeSeriesDataService series,
             FakeEpisodeDataService episodes,
             _,
             FakeCoverImageDataService covers) = Build();

            Series fromProvider = new() { Title = "Vom Anbieter", AppleMusicArtistId = "77" };
            await series.AddAsync(fromProvider, TestContext.Current.CancellationToken);

            Episode episode = new() { Title = "Folge 1", SeriesId = fromProvider.Id };
            await episodes.AddAsync(episode, TestContext.Current.CancellationToken);

            await covers.SetCoverAsync(CoverEntityTypes.Series, fromProvider.Id, [1, 2, 3], cancellationToken: TestContext.Current.CancellationToken);
            await covers.SetCoverAsync(CoverEntityTypes.Episode, episode.Id, [4, 5, 6], cancellationToken: TestContext.Current.CancellationToken);

            await reset.ClearLocalAssignmentsAsync();

            Assert.Equal(0, await covers.CountAsync(TestContext.Current.CancellationToken));
        }
    }
}
