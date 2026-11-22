using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="LocalArtistsViewModel"/> mit Schwerpunkt auf dem Zusammenspiel von
    /// Live-Kachel-Update während des Scans und Cover-Nachbau.
    /// </summary>
    public sealed class LocalArtistsViewModelTests
    {
        [Fact]
        public async Task AppendArtistCardAsync_WhenTheDatabaseFails_SwallowsAndSkipsTheCard()
        {
            // Läuft losgelöst aus dem Scan-Ereignis. Ein Fehler, der hier entkäme, bliebe
            // unbeobachtet; die Kachel erscheint spätestens beim abschließenden Neuladen.
            ServiceCollection services = new();
            _ = services.AddScoped<IEpisodeDataService>(_ => throw new InvalidOperationException("Datenbank gesperrt"));
            ServiceProvider provider = services.BuildServiceProvider();
            LocalArtistsViewModel vm = new(provider.GetRequiredService<IServiceScopeFactory>(), new SeriesCoverBuilder(new FakeCoverService()));

            Exception? error = await Record.ExceptionAsync(() => vm.AppendArtistCardAsync(MakeSeries(TestIds.SeriesA, "TKKG")));

            Assert.Null(error);
            Assert.Empty(vm.AllArtists);
        }

        private static LocalArtistsViewModel BuildViewModel(
            out FakeCoverService coverService,
            out FakeEpisodeDataService episodeService)
        {
            episodeService = new FakeEpisodeDataService();
            coverService = new FakeCoverService();

            ServiceCollection services = new();
            IEpisodeDataService episodes = episodeService;
            _ = services.AddScoped<IEpisodeDataService>(_ => episodes);
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistsViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new SeriesCoverBuilder(coverService));
        }

        private static Series MakeSeries(Guid id, string title)
        {
            Series series = new() { Title = title, LocalFolderPath = null };
            // Id hat nur einen protected Setter (EF Core vergibt sie sonst erst bei SaveChanges);
            // der Test setzt sie per Reflection auf einen festen Wert.
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            idProp.SetValue(series, id);
            return series;
        }

        private static Episode MakeEpisode(Guid seriesId, int number, bool local) => new()
        {
            SeriesId = seriesId,
            Title = $"Folge {number}",
            EpisodeNumber = number,
            LocalFolderPath = local ? $@"C:\Serie\Folge{number}" : null
        };

        [Fact]
        public async Task AppendArtistCardAsync_ExistingCardWithoutCover_AttemptsCoverRebuild()
        {
            // Eine schon im Live-Scan angelegte Karte ohne Cover muss beim erneuten
            // SeriesSynced-Report einen Cover-Nachbau auslösen. Kehrte der Existing-Zweig nach
            // UpdateCounts zurück, ohne das Cover zu bauen, bliebe die Serie die ganze Sitzung
            // ohne Bild.
            LocalArtistsViewModel vm = BuildViewModel(out FakeCoverService cover, out FakeEpisodeDataService episodes);
            Series series = MakeSeries(TestIds.SeriesA, "Benjamin Blümchen");
            await episodes.AddRangeAsync([
                MakeEpisode(series.Id, 1, local: true),
                MakeEpisode(series.Id, 2, local: true),
                MakeEpisode(series.Id, 3, local: false)
            ], TestContext.Current.CancellationToken);

            // Erster Report: Karte wird neu angelegt (Cover-Anforderung #1, liefert null).
            await vm.AppendArtistCardAsync(series);
            _ = Assert.Single(cover.SeriesCoverRequests);

            // Zweiter Report derselben Serie: Karte existiert, Cover ist null → Nachbau (#2).
            await vm.AppendArtistCardAsync(series);

            Assert.Equal(2, cover.SeriesCoverRequests.Count);
            Assert.All(cover.SeriesCoverRequests, id => Assert.Equal(series.Id, id));
            _ = Assert.Single(vm.AllArtists);
        }

        [Fact]
        public async Task AppendArtistCardAsync_NewSeries_LandsInItsSection()
        {
            LocalArtistsViewModel vm = BuildViewModel(out _, out _);
            await vm.AppendArtistCardAsync(MakeSeries(TestIds.SeriesA, "Ahoi"));
            await vm.AppendArtistCardAsync(MakeSeries(TestIds.SeriesB, "Zorro"));

            // Eine während des Einlesens gemeldete Serie gehört unter ihren Buchstaben, nicht
            // ans Ende hinter Z.
            await vm.AppendArtistCardAsync(MakeSeries(Guid.NewGuid(), "Ärger im Paradies"));

            Assert.Equal(["Ahoi", "Ärger im Paradies", "Zorro"], vm.AllArtists.Select(a => a.Title));
        }

        [Fact]
        public async Task AppendArtistCardAsync_ExistingCard_LiveUpdatesCounts()
        {
            // Sichert das 67f7adb-Verhalten: Zähler ziehen live nach, ohne die Kachel zu duplizieren.
            LocalArtistsViewModel vm = BuildViewModel(out _, out FakeEpisodeDataService episodes);
            Series series = MakeSeries(TestIds.SeriesA, "Die drei ???");

            // Phase 3: Serie gemeldet, Episoden noch nicht persistiert → 0 / 0.
            await vm.AppendArtistCardAsync(series);
            LocalArtistCardViewModel card = Assert.Single(vm.AllArtists);
            Assert.Equal(0, card.TotalEpisodeCount);
            Assert.Equal("0 / 0", card.CountText);

            // Phase 4: Episoden liegen jetzt vor, gleiche Serie erneut gemeldet.
            await episodes.AddRangeAsync([
                MakeEpisode(series.Id, 1, local: true),
                MakeEpisode(series.Id, 2, local: true),
                MakeEpisode(series.Id, 3, local: false)
            ], TestContext.Current.CancellationToken);
            await vm.AppendArtistCardAsync(series);

            _ = Assert.Single(vm.AllArtists);
            Assert.Equal(2, card.LocalEpisodeCount);
            Assert.Equal(3, card.TotalEpisodeCount);
            Assert.Equal("2 / 3", card.CountText);
        }
    }
}
