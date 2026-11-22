using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System.Globalization;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="AnnouncedViewModel"/>: Gruppierung nach Monat in Ortszeit,
    /// Reihenfolge und die Hinweise bei leerer Liste.
    /// </summary>
    public sealed class AnnouncedViewModelTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>Liefert eine feste Liste angekündigter Kacheln.</summary>
        private sealed class FakeSource : IReleaseCalendarSource
        {
            public IReadOnlyList<ReleaseCalendarItemViewModel> Announced { get; set; } = [];

            public Func<CancellationToken, Task<IReadOnlyList<ReleaseCalendarItemViewModel>>>? Override { get; set; }

            public Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadAnnouncedAsync(CancellationToken cancellationToken)
                => Override?.Invoke(cancellationToken) ?? Task.FromResult(Announced);

            public Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadMonthAsync(ReleaseCalendarMonth month, CancellationToken cancellationToken)
                => throw new NotSupportedException("Die Seite lädt keine einzelnen Monate.");

            public Task<ReleaseCalendarMonth?> GetLatestMonthAsync(CancellationToken cancellationToken)
                => throw new NotSupportedException("Die Seite braucht den letzten Monat nicht.");
        }

        private readonly FakeSource _source = new();

        private AnnouncedViewModel Build() => new(_source, Berlin, German);

        private static ReleaseCalendarItemViewModel Item(string title, DateTime releaseUtc) => new(
            new ReleaseCalendarItemData
            {
                SeriesId = Guid.NewGuid(),
                SeriesName = "TKKG",
                EpisodeTitle = title,
                DateLine = "Tag",
                BadgeText = "Angekündigt",
                ReleaseDateUtc = releaseUtc,
                IsAnnounced = true,
                Availability = ReleaseAvailability.Missing,
            });

        [Fact]
        public async Task LoadAsync_GroupsByLocalMonthInOrder()
        {
            _source.Announced =
            [
                Item("Folge 240", new DateTime(2026, 11, 28, 8, 0, 0, DateTimeKind.Utc)),
                Item("Folge 241", new DateTime(2026, 12, 12, 8, 0, 0, DateTimeKind.Utc)),
                // 31. Dezember, 23:30 UTC ist in Berlin schon der 1. Januar.
                Item("Folge 242", new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc)),
                Item("Folge 243", new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc))
            ];
            AnnouncedViewModel vm = Build();

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["November 2026", "Dezember 2026", "Januar 2027"], vm.Groups.Select(g => g.Label));
            Assert.Equal(["Folge 242", "Folge 243"], vm.Groups[2].Items.Select(i => i.EpisodeTitle));
            Assert.Equal(Visibility.Collapsed, vm.EmptyHintVisibility);
            Assert.Equal(Visibility.Collapsed, vm.LoadingVisibility);
        }

        [Fact]
        public async Task LoadAsync_EmptyList_ShowsTheHint()
        {
            AnnouncedViewModel vm = Build();
            Assert.Equal(Visibility.Collapsed, vm.EmptyHintVisibility);

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Empty(vm.Groups);
            Assert.Equal(Visibility.Visible, vm.EmptyHintVisibility);
        }

        [Fact]
        public async Task LoadAsync_WhileLoading_ShowsOnlyTheProgress()
        {
            TaskCompletionSource<IReadOnlyList<ReleaseCalendarItemViewModel>> pending = new();
            _source.Override = _ => pending.Task;
            AnnouncedViewModel vm = Build();

            Task load = vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.True(vm.IsLoading);
            Assert.Equal(Visibility.Visible, vm.LoadingVisibility);
            Assert.Equal(Visibility.Collapsed, vm.EmptyHintVisibility);

            pending.SetResult([]);
            await load;

            Assert.False(vm.IsLoading);
        }

        [Fact]
        public async Task LoadAsync_SourceFails_EndsLoadingAndPassesTheErrorOn()
        {
            _source.Override = _ => Task.FromException<IReadOnlyList<ReleaseCalendarItemViewModel>>(new InvalidOperationException("Datenbank gesperrt"));
            AnnouncedViewModel vm = Build();

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.LoadAsync(TestContext.Current.CancellationToken));

            Assert.False(vm.IsLoading);
            Assert.Equal(Visibility.Collapsed, vm.EmptyHintVisibility);
        }
    }
}
