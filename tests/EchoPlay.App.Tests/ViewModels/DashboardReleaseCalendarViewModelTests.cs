using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System.Globalization;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="DashboardReleaseCalendarViewModel"/>: Startmonat, Grenzen des
    /// Umschalters und die Hinweise bei leerem Monat.
    /// </summary>
    public sealed class DashboardReleaseCalendarViewModelTests
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>Liefert je Monat eine feste Kachelliste und merkt sich die Abfragen.</summary>
        private sealed class FakeSource : IReleaseCalendarSource
        {
            public Dictionary<ReleaseCalendarMonth, IReadOnlyList<ReleaseCalendarItemViewModel>> Months { get; } = [];

            public ReleaseCalendarMonth? Latest { get; set; }

            public List<ReleaseCalendarMonth> Requested { get; } = [];

            /// <summary>Ersetzt das Laden für einzelne Fälle: hängen bleiben oder scheitern.</summary>
            public Func<ReleaseCalendarMonth, CancellationToken, Task<IReadOnlyList<ReleaseCalendarItemViewModel>>?>? Override { get; set; }

            public Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadMonthAsync(ReleaseCalendarMonth month, CancellationToken cancellationToken)
            {
                Requested.Add(month);
                Task<IReadOnlyList<ReleaseCalendarItemViewModel>>? overridden = Override?.Invoke(month, cancellationToken);
                if (overridden is not null)
                {
                    return overridden;
                }

                return Task.FromResult(Months.TryGetValue(month, out IReadOnlyList<ReleaseCalendarItemViewModel>? items) ? items : []);
            }

            public Task<ReleaseCalendarMonth?> GetLatestMonthAsync(CancellationToken cancellationToken) => Task.FromResult(Latest);

            public Task<IReadOnlyList<ReleaseCalendarItemViewModel>> LoadAnnouncedAsync(CancellationToken cancellationToken)
                => throw new NotSupportedException("Der Abschnitt der Startseite lädt nur Monate.");
        }

        private readonly FakeSource _source = new();

        // 19. November 2026: laufender Monat November, Vormonat Oktober, Folgemonat Dezember.
        private readonly FakeClock _clock = new() { UtcNow = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc) };

        private readonly CapturingLogger _log = new();

        private DashboardReleaseCalendarViewModel Build()
            => new(_source, _clock, Berlin, German, _log) { IsAvailable = true };

        private static ReleaseCalendarItemViewModel Item(string title) => new(
            new ReleaseCalendarItemData
            {
                SeriesId = Guid.NewGuid(),
                SeriesName = "TKKG",
                EpisodeTitle = title,
                DateLine = "14. November · Nr. 230",
                ReleaseDateUtc = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc),
                Availability = ReleaseAvailability.Missing,
            });

        [Fact]
        public async Task LoadAsync_ShowsTheCurrentMonth()
        {
            _source.Months[new ReleaseCalendarMonth(2026, 11)] = [Item("Folge 230")];
            DashboardReleaseCalendarViewModel vm = Build();

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal("November 2026", vm.MonthLabel);
            Assert.Equal("Folge 230", Assert.Single(vm.Items).EpisodeTitle);
            Assert.Equal(Visibility.Visible, vm.ItemsVisibility);
            Assert.Equal(Visibility.Collapsed, vm.EmptyHintVisibility);
        }

        [Fact]
        public async Task PreviousMonth_StopsAtThePreviousMonth()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);

            vm.PreviousMonthCommand.Execute(null);

            Assert.Equal(new ReleaseCalendarMonth(2026, 10), vm.SelectedMonth);
            Assert.Equal("Oktober 2026", vm.MonthLabel);
            Assert.False(vm.PreviousMonthCommand.CanExecute(null));
            Assert.True(vm.NextMonthCommand.CanExecute(null));
        }

        [Fact]
        public async Task NextMonth_WithoutLaterAnnouncements_StopsAtTheFollowingMonth()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);

            vm.NextMonthCommand.Execute(null);

            Assert.Equal(new ReleaseCalendarMonth(2026, 12), vm.SelectedMonth);
            Assert.False(vm.NextMonthCommand.CanExecute(null));
        }

        [Fact]
        public async Task NextMonth_ReachesTheLatestAnnouncementAcrossTheYear()
        {
            _source.Latest = new ReleaseCalendarMonth(2027, 2);
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);

            vm.NextMonthCommand.Execute(null);
            vm.NextMonthCommand.Execute(null);
            vm.NextMonthCommand.Execute(null);

            Assert.Equal(new ReleaseCalendarMonth(2027, 2), vm.SelectedMonth);
            Assert.Equal("Februar 2027", vm.MonthLabel);
            Assert.False(vm.NextMonthCommand.CanExecute(null));
        }

        [Fact]
        public async Task LoadAsync_AgainAfterSwitching_KeepsTheChosenMonth()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            vm.NextMonthCommand.Execute(null);

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new ReleaseCalendarMonth(2026, 12), vm.SelectedMonth);
        }

        [Fact]
        public async Task EmptyMonth_ShowsTheEmptyHint()
        {
            DashboardReleaseCalendarViewModel vm = Build();

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(Visibility.Collapsed, vm.ItemsVisibility);
            Assert.Equal(Visibility.Visible, vm.EmptyHintVisibility);
            Assert.Equal(Visibility.Collapsed, vm.RefreshingHintVisibility);
        }

        [Fact]
        public async Task EmptyMonth_WhileRefreshing_ShowsTheRefreshingHintInstead()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            vm.IsRefreshing = true;

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(Visibility.Visible, vm.RefreshingHintVisibility);
            Assert.Equal(Visibility.Collapsed, vm.EmptyHintVisibility);
        }

        [Fact]
        public void SectionVisibility_FollowsAvailability()
        {
            DashboardReleaseCalendarViewModel vm = Build();

            vm.IsAvailable = false;

            Assert.Equal(Visibility.Collapsed, vm.SectionVisibility);
        }

        [Fact]
        public async Task LoadAsync_WhenTheLatestAnnouncementVanished_PullsTheMonthBack()
        {
            _source.Latest = new ReleaseCalendarMonth(2027, 2);
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            vm.NextMonthCommand.Execute(null);
            vm.NextMonthCommand.Execute(null);
            vm.NextMonthCommand.Execute(null);

            // Der nächste Aufbau kennt die Ankündigung im Februar nicht mehr.
            _source.Latest = null;
            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new ReleaseCalendarMonth(2026, 12), vm.SelectedMonth);
        }

        [Fact]
        public async Task LoadAsync_AfterTheMonthChanged_PullsAnOldMonthForward()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            vm.PreviousMonthCommand.Execute(null);

            // Die Anwendung bleibt bis Januar offen: Oktober liegt dann vor dem Vormonat.
            _clock.UtcNow = new DateTime(2027, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new ReleaseCalendarMonth(2026, 12), vm.SelectedMonth);
        }

        [Fact]
        public async Task SwitchingAgain_WhileAMonthLoads_DropsTheSlowerOne()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            _source.Override = (month, ct) => month == new ReleaseCalendarMonth(2026, 12)
                ? WaitForeverAsync(ct)
                : null;

            vm.NextMonthCommand.Execute(null);
            vm.PreviousMonthCommand.Execute(null);

            // Das Abbrechen des hängenden Ladens läuft über einen Rückruf im Thread-Pool; der
            // zweite Wechsel endet deshalb erst danach.
            await Eventually.TrueAsync(() => vm.SelectedMonth == new ReleaseCalendarMonth(2026, 10));

            Assert.Equal(new ReleaseCalendarMonth(2026, 10), vm.SelectedMonth);
            Assert.DoesNotContain(_log.Entries, e => e.Level == "Warning");
        }

        [Fact]
        public async Task SwitchingMonth_WhenLoadingFails_StaysAndLogs()
        {
            DashboardReleaseCalendarViewModel vm = Build();
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            _source.Override = (_, _) => Task.FromException<IReadOnlyList<ReleaseCalendarItemViewModel>>(
                new InvalidOperationException("Datenbank gesperrt"));

            vm.NextMonthCommand.Execute(null);

            Assert.Equal(new ReleaseCalendarMonth(2026, 11), vm.SelectedMonth);
            Assert.Contains(_log.Entries, e => e.Level == "Warning");
        }

        [Fact]
        public void IsRefreshing_ReportsTheSetValue()
        {
            DashboardReleaseCalendarViewModel vm = Build();

            vm.IsRefreshing = true;

            Assert.True(vm.IsRefreshing);
        }

        private static async Task<IReadOnlyList<ReleaseCalendarItemViewModel>> WaitForeverAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }

        [Fact]
        public async Task CurrentMonth_FollowsLocalTimeOnTheLastEvening()
        {
            // 30. November, 23:30 UTC ist in Berlin schon Dezember.
            _clock.UtcNow = new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc);
            DashboardReleaseCalendarViewModel vm = Build();

            await vm.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new ReleaseCalendarMonth(2026, 12), vm.SelectedMonth);
        }
    }
}
