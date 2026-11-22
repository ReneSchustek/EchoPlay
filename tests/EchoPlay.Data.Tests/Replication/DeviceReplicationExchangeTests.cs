using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication.Exchange;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Abgleich zwischen simulierten Geräten über einen gemeinsamen Ordner: jedes mit eigener
    /// Datenbank, eigener Geräte-Kennung und eigener, gegeneinander verstellter Uhr.
    /// </summary>
    public sealed class DeviceReplicationExchangeTests : IDisposable
    {
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private static readonly DateTime Noon = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_replication_{Guid.NewGuid():N}");

        public DeviceReplicationExchangeTests() => _ = Directory.CreateDirectory(_root);

        private string Shared => Path.Combine(_root, "Gemeinsam");

        [Fact]
        public async Task ChangeOnOneDevice_ArrivesOnTheOther_AndIsNotEchoedBack()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();

            await a.EditAsync(c => c.PlaybackStates.Single().IsCompleted = true, ct);
            Assert.True(await a.PublishAsync(ct) > 0);
            ReceiveResult received = await b.ReceiveAsync(ct);

            Assert.Equal(1, received.Files);
            Assert.Equal(1, received.Changed);
            Assert.Empty(received.Problems);
            await using EchoPlayDbContext context = b.Open();
            Assert.True((await context.PlaybackStates.SingleAsync(ct)).IsCompleted);
            Assert.Equal(0, await b.PublishAsync(ct));
        }

        [Fact]
        public async Task ConcurrentChanges_WithSkewedClocks_Converge()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync(skewB: TimeSpan.FromHours(-3));

            await a.EditAsync(c =>
            {
                c.Series.Single().IsFavorite = true;
                PlaybackState state = c.PlaybackStates.Single();
                state.LastPosition = TimeSpan.FromMinutes(10);
                state.LastPlayedAt = Noon.AddMinutes(5);
            }, ct);
            await b.EditAsync(c =>
            {
                c.Series.Single().Description = "von B";
                PlaybackState state = c.PlaybackStates.Single();
                state.IsCompleted = true;
                state.CompletedAt = Noon;
                state.LastPosition = TimeSpan.FromMinutes(2);
                state.LastPlayedAt = Noon;
            }, ct);

            _ = await a.PublishAsync(ct);
            _ = await b.PublishAsync(ct);
            _ = await a.ReceiveAsync(ct);
            _ = await b.ReceiveAsync(ct);

            List<ChangeRecord> onA = await a.RecordsAsync(ct);
            List<ChangeRecord> onB = await b.RecordsAsync(ct);
            Assert.Equal(onA.OrderBy(r => r.Kind + r.Key), onB.OrderBy(r => r.Kind + r.Key));

            ChangeRecord playback = onA.Single(r => r.Kind == ChangeKinds.PlaybackState);
            Assert.Equal("true", playback.Field(ChangeMerge.IsCompletedField));
            Assert.Equal(FieldText.Format(TimeSpan.FromMinutes(10)), playback.Field(ChangeMerge.LastPositionField));
        }

        [Fact]
        public async Task ReadingTwice_ChangesNothing()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            await a.EditAsync(c => c.Series.Single().IsWatched = true, ct);
            _ = await a.PublishAsync(ct);
            _ = await b.ReceiveAsync(ct);

            await using (EchoPlayDbContext context = b.Open())
            {
                (await context.ReplicationPeers.SingleAsync(p => p.DeviceId == a.Id, ct)).LastSequence = 0;
                _ = await context.SaveChangesAsync(ct);
            }

            ReceiveResult again = await b.ReceiveAsync(ct);

            // Die Ausgangsdatei und die Änderung, beide schon bekannt.
            Assert.Equal(2, again.Files);
            Assert.Equal(0, again.Changed);
        }

        [Fact]
        public async Task Deletion_TravelsAsATombstone_AndALaterRestoreWins()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();

            await a.EditAsync(c => c.WatchedTitles.Single().MarkAsDeleted(Noon), ct);
            _ = await a.PublishAsync(ct);
            _ = await b.ReceiveAsync(ct);
            await using (EchoPlayDbContext context = b.Open())
            {
                Assert.True((await context.WatchedTitles.IgnoreQueryFilters().SingleAsync(ct)).IsDeleted);
            }

            await b.EditAsync(c =>
            {
                WatchedTitle title = c.WatchedTitles.IgnoreQueryFilters().Single();
                title.MarkAsRestored();
                title.Title = "TKKG neu";
            }, ct);
            _ = await b.PublishAsync(ct);
            _ = await a.ReceiveAsync(ct);

            await using EchoPlayDbContext onA = a.Open();
            WatchedTitle restored = await onA.WatchedTitles.IgnoreQueryFilters().SingleAsync(ct);
            Assert.False(restored.IsDeleted);
            Assert.Equal("TKKG neu", restored.Title);
        }

        [Fact]
        public async Task NewSeriesWithEpisodeAndPlayback_IsCreatedOnTheOtherDevice()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            await a.EditAsync(c =>
            {
                Series series = new() { Title = "Die drei ???", IsOnlineImported = true, AppleMusicArtistId = "999", IsSubscribed = true };
                Episode episode = new() { Series = series, Title = "Folge 1", EpisodeNumber = 1, AppleMusicAlbumId = "1001" };
                _ = c.Series.Add(series);
                _ = c.Episodes.Add(episode);
                _ = c.PlaybackStates.Add(new PlaybackState { Episode = episode, LastPosition = TimeSpan.FromMinutes(3), LastPlayedAt = Noon });
            }, ct);

            _ = await a.PublishAsync(ct);
            ReceiveResult result = await b.ReceiveAsync(ct);

            Assert.Equal(3, result.Changed);
            await using EchoPlayDbContext context = b.Open();
            Series created = await context.Series.SingleAsync(s => s.AppleMusicArtistId == "999", ct);
            Episode episodeOnB = await context.Episodes.SingleAsync(e => e.SeriesId == created.Id, ct);
            Assert.Equal(TimeSpan.FromMinutes(3), (await context.PlaybackStates.SingleAsync(p => p.EpisodeId == episodeOnB.Id, ct)).LastPosition);
            Assert.Null(created.LocalFolderPath);
        }

        [Fact]
        public async Task DashboardOrder_TheLatestListWins()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync(withSecondSeries: true);

            await a.EditAsync(c =>
            {
                List<Series> series = [.. c.Series.OrderBy(s => s.Title)];
                foreach (DashboardPosition old in c.DashboardPositions)
                {
                    _ = c.DashboardPositions.Remove(old);
                }

                _ = c.DashboardPositions.Add(new DashboardPosition { SeriesId = series[1].Id, Section = "Favorites", Position = 0 });
                _ = c.DashboardPositions.Add(new DashboardPosition { SeriesId = series[0].Id, Section = "Favorites", Position = 1 });
            }, ct);
            _ = await a.PublishAsync(ct);
            _ = await b.ReceiveAsync(ct);

            await using EchoPlayDbContext context = b.Open();
            List<string> order = await context.DashboardPositions.OrderBy(p => p.Position).Select(p => p.Series.Title).ToListAsync(ct);
            Assert.Equal(["TKKG", "Benjamin Blümchen"], order);
        }

        [Fact]
        public async Task BrokenTruncatedAndForeignFiles_StopThatDevice_WithoutTouchingTheLibrary()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            await a.EditAsync(c => c.Series.Single().IsWatched = true, ct);
            _ = await a.PublishAsync(ct);
            string folder = ExchangeLayout.DeviceFolder(Shared, a.Id);
            string second = Path.Combine(folder, ExchangeLayout.FileName(3, isSnapshot: false));
            string first = await File.ReadAllTextAsync(Path.Combine(folder, ExchangeLayout.FileName(2, false)), ct);
            await File.WriteAllTextAsync(second, first[..(first.Length / 2)], ct);
            await File.WriteAllTextAsync(Path.Combine(folder, "notizen.txt"), "fremd", ct);

            ReceiveResult result = await b.ReceiveAsync(ct);

            Assert.Equal(1, result.Files);
            Assert.Contains("000000000003.json", Assert.Single(result.Problems), StringComparison.Ordinal);
            await using (EchoPlayDbContext context = b.Open())
            {
                ReplicationPeer peer = await context.ReplicationPeers.SingleAsync(p => p.DeviceId == a.Id, ct);
                Assert.Equal(2, peer.LastSequence);
                Assert.True((await context.Series.SingleAsync(ct)).IsWatched);
                Assert.NotNull(peer.LastError);
            }

            // Eine Datei mit fremder Kennung im Ordner von A gehört nicht hierher.
            Guid stranger = Guid.NewGuid();
            await File.WriteAllTextAsync(second, new ChangeBatch(ChangeBatch.CurrentFormatVersion, stranger, 3, false, []).ToJson(), ct);
            Assert.Contains("gehört nicht", Assert.Single((await b.ReceiveAsync(ct)).Problems), StringComparison.Ordinal);

            // Sobald die Datei vollständig ist, geht es weiter, und der Fehler verschwindet.
            await File.WriteAllTextAsync(second, new ChangeBatch(ChangeBatch.CurrentFormatVersion, a.Id, 3, false, []).ToJson(), ct);
            ReceiveResult resumed = await b.ReceiveAsync(ct);
            Assert.Equal(1, resumed.Files);
            Assert.Empty(resumed.Problems);
        }

        [Fact]
        public async Task GapInTheSequence_WaitsForTheMissingFile()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            string folder = ExchangeLayout.DeviceFolder(Shared, a.Id);
            _ = Directory.CreateDirectory(folder);
            // Nummer 1 ist die Ausgangsdatei; 2 wird noch übertragen, 3 ist schon da.
            await File.WriteAllTextAsync(Path.Combine(folder, ExchangeLayout.FileName(3, false)), new ChangeBatch(ChangeBatch.CurrentFormatVersion, a.Id, 3, false, []).ToJson(), ct);
            await File.WriteAllTextAsync(Path.Combine(folder, ExchangeLayout.FileName(2, false) + ExchangeLayout.PartialSuffix), "halb", ct);

            ReceiveResult result = await b.ReceiveAsync(ct);

            Assert.Equal(0, result.Files);
            Assert.Empty(result.Problems);
        }

        [Fact]
        public async Task Compaction_ReplacesOwnFiles_AndANewDeviceStartsFromTheSnapshot()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, _) = await CreatePairAsync();
            for (int i = 0; i < 3; i++)
            {
                int minutes = i;
                await a.EditAsync(c => c.PlaybackStates.Single().LastPosition = TimeSpan.FromMinutes(minutes + 1), ct);
                _ = await a.PublishAsync(ct);
            }

            // Vier eigene Dateien: die Ausgangsdatei und drei Änderungen.
            Assert.False(await a.CompactAsync(threshold: 5, ct));
            Assert.True(await a.CompactAsync(threshold: 4, ct));

            string[] left = [.. Directory.GetFiles(ExchangeLayout.DeviceFolder(Shared, a.Id)).Select(Path.GetFileName)!];
            Assert.Equal([ExchangeLayout.FileName(4, isSnapshot: true)], left);

            Device fresh = await CreateDeviceAsync("neu", TimeSpan.Zero);
            ReceiveResult result = await fresh.ReceiveAsync(ct);

            Assert.Equal(1, result.Files);
            await using EchoPlayDbContext context = fresh.Open();
            Assert.Equal(TimeSpan.FromMinutes(3), (await context.PlaybackStates.SingleAsync(ct)).LastPosition);
            Assert.Equal(1, await context.WatchedTitles.CountAsync(ct));
        }

        [Fact]
        public async Task HintsAndSharedSettings_Travel_DeviceBoundSettingsStay()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            await a.EditAsync(c =>
            {
                _ = c.DialogSuppressions.Add(new DialogSuppression { Key = "ConfirmDelete" });
                AppSettings settings = c.AppSettings.Single();
                settings.ActiveTheme = "Ruhrcoder";
                settings.Volume = 0.1;
            }, ct);

            _ = await a.PublishAsync(ct);
            _ = await b.ReceiveAsync(ct);

            await using EchoPlayDbContext context = b.Open();
            Assert.Equal("ConfirmDelete", (await context.DialogSuppressions.SingleAsync(ct)).Key);
            AppSettings onB = await context.AppSettings.SingleAsync(ct);
            Assert.Equal("Ruhrcoder", onB.ActiveTheme);
            Assert.Equal(0.8, onB.Volume);
        }

        [Fact]
        public async Task SeveralRowsForOneKey_TheActiveOneCounts()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            await b.EditAsync(c =>
            {
                WatchedTitle old = new() { Title = "TKKG alt", NormalizedTitle = "tkkg" };
                old.MarkAsDeleted(Noon);
                _ = c.WatchedTitles.Add(old);
            }, ct);
            await a.EditAsync(c => c.WatchedTitles.Single().Title = "TKKG von A", ct);

            _ = await a.PublishAsync(ct);
            _ = await b.ReceiveAsync(ct);

            await using EchoPlayDbContext context = b.Open();
            Assert.Equal("TKKG von A", (await context.WatchedTitles.SingleAsync(t => !t.IsDeleted, ct)).Title);
            Assert.Equal("TKKG alt", (await context.WatchedTitles.IgnoreQueryFilters().SingleAsync(t => t.IsDeleted, ct)).Title);
        }

        [Fact]
        public async Task RecordsWithoutParentOrWithUnknownFields_AreSkippedOrPartlyTaken()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            HybridTimestamp stamp = new(new DateTimeOffset(Noon.AddHours(1)).ToUnixTimeMilliseconds(), 0, a.Id);
            ChangeRecord orphan = new(ChangeKinds.Episode, "online:apple:nix|apple:1", "online:apple:nix", stamp, false,
                new Dictionary<string, string?> { ["Title"] = "Folge ohne Serie" });
            ChangeRecord odd = new(ChangeKinds.Series, "online:apple:123", null, stamp, false,
                new Dictionary<string, string?> { ["Title"] = "TKKG", ["IsOnlineImported"] = "true", ["AppleMusicArtistId"] = "123", ["IsFavorite"] = "vielleicht", ["Unbekannt"] = "x" });
            await WriteBatchAsync(a.Id, 2, orphan, odd);

            ReceiveResult result = await b.ReceiveAsync(ct);

            Assert.Equal(1, result.Changed);
            await using EchoPlayDbContext context = b.Open();
            Assert.Equal(1, await context.Episodes.CountAsync(ct));
            Assert.False((await context.Series.SingleAsync(ct)).IsFavorite);
        }

        [Fact]
        public async Task UnreadableFiles_AreReportedAsProblems()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            string file = Path.Combine(ExchangeLayout.DeviceFolder(Shared, a.Id), ExchangeLayout.FileName(2, false));
            await File.WriteAllBytesAsync(file, [0x7B, 0xC3, 0x28, 0x7D], ct);

            Assert.Contains("000000000002", Assert.Single((await b.ReceiveAsync(ct)).Problems), StringComparison.Ordinal);

            await File.WriteAllTextAsync(file, "{}", ct);
            await using FileStream locked = new(file, FileMode.Open, FileAccess.Read, FileShare.None);
            _ = Assert.Single((await b.ReceiveAsync(ct)).Problems);
        }

        [Fact]
        public async Task CompactWithoutOwnFolder_DoesNothing()
        {
            Device a = await CreateDeviceAsync("ohne", TimeSpan.Zero);

            Assert.False(await a.CompactAsync(threshold: 1, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RemovedDevice_IsNoLongerRead()
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            (Device a, Device b) = await CreatePairAsync();
            await a.EditAsync(c => c.Series.Single().IsWatched = true, ct);
            _ = await a.PublishAsync(ct);
            await using (EchoPlayDbContext context = b.Open())
            {
                Assert.True(await new EchoPlay.Data.Services.ReplicationPeerDataService(context).RemoveAsync(a.Id, ct));
            }

            ReceiveResult result = await b.ReceiveAsync(ct);

            Assert.Equal(0, result.Files);
            await using EchoPlayDbContext onB = b.Open();
            Assert.False((await onB.Series.SingleAsync(ct)).IsWatched);
        }

        [Fact]
        public async Task ReceiveWithoutSharedFolder_DoesNothing()
        {
            Device a = await CreateDeviceAsync("allein", TimeSpan.Zero);

            ReceiveResult result = await a.ReceiveAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new ReceiveResult(0, 0, 0, []), result with { Problems = [] });
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        /// <summary>
        /// Zwei Geräte mit demselben Ausgangsstand, wie nach einem eingespielten Datenpaket: Die
        /// Datenbank von A wird für B kopiert, die Geräte-Kennung ist verschieden.
        /// </summary>
        /// <summary>
        /// Der Ausgangsbestand von Gerät A: eine Online-Serie mit Folge, Hörstand, überwachtem
        /// Titel, Startseiten-Platz und Einstellungen, wahlweise eine zweite Serie dazu.
        /// </summary>
        private static void SeedLibrary(EchoPlayDbContext c, bool withSecondSeries)
        {
            Series series = new() { Title = "TKKG", IsOnlineImported = true, AppleMusicArtistId = "123" };
            Episode episode = new() { Series = series, Title = "Folge 1", EpisodeNumber = 1, AppleMusicAlbumId = "501" };
            _ = c.Series.Add(series);
            _ = c.Episodes.Add(episode);
            _ = c.PlaybackStates.Add(new PlaybackState { Episode = episode });
            _ = c.WatchedTitles.Add(new WatchedTitle { Title = "TKKG", NormalizedTitle = "tkkg" });
            _ = c.DashboardPositions.Add(new DashboardPosition { Series = series, Section = "Favorites", Position = 0 });
            _ = c.AppSettings.Add(new AppSettings { Volume = 0.5 });
            if (withSecondSeries)
            {
                Series second = new() { Title = "Benjamin Blümchen", IsOnlineImported = true, AppleMusicArtistId = "456" };
                _ = c.Series.Add(second);
                _ = c.DashboardPositions.Add(new DashboardPosition { Series = second, Section = "Favorites", Position = 1 });
            }
        }

        private async Task<(Device A, Device B)> CreatePairAsync(TimeSpan? skewB = null, bool withSecondSeries = false)
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Device a = await CreateDeviceAsync("a", TimeSpan.Zero);
            await a.EditAsync(c => SeedLibrary(c, withSecondSeries), ct);

            SqliteConnection.ClearAllPools();
            Device b = await CreateDeviceAsync("b", skewB ?? TimeSpan.Zero, copyFrom: a);

            // A hat seinen Ausgangsstand selbst geschrieben; er gilt als schon verteilt.
            _ = await a.PublishAsync(ct);
            await using (EchoPlayDbContext context = b.Open())
            {
                _ = context.ReplicationPeers.Add(new ReplicationPeer { DeviceId = a.Id, LastSequence = 1 });
                (await context.AppSettings.SingleAsync(ct)).Volume = 0.8;
                _ = await context.SaveChangesAsync(ct);
            }

            return (a, b);
        }

        /// <summary>Legt eine Protokolldatei von Hand in den Ordner eines Geräts.</summary>
        private async Task WriteBatchAsync(Guid device, long sequence, params ChangeRecord[] records)
        {
            string folder = ExchangeLayout.DeviceFolder(Shared, device);
            _ = Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, ExchangeLayout.FileName(sequence, false)),
                new ChangeBatch(ChangeBatch.CurrentFormatVersion, device, sequence, false, records).ToJson(),
                TestContext.Current.CancellationToken);
        }

        private async Task<Device> CreateDeviceAsync(string name, TimeSpan skew, Device? copyFrom = null)
        {
            string directory = Path.Combine(_root, name);
            _ = Directory.CreateDirectory(directory);
            if (copyFrom is not null)
            {
                File.Copy(Path.Combine(copyFrom.Directory, "echoplay.db"), Path.Combine(directory, "echoplay.db"));
            }

            Device device = new(directory, Shared, skew);
            await using EchoPlayDbContext context = device.Open();
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            return device;
        }

        /// <summary>Ein simuliertes Gerät: Ordner, Kennung, verstellte Uhr, Datenbank mit Stempel-Interceptor.</summary>
        private sealed class Device
        {
            private readonly FileDeviceIdentity _identity;
            private readonly HybridLogicalClock _clock;
            private readonly string _shared;

            public Device(string directory, string shared, TimeSpan skew)
            {
                Directory = directory;
                _shared = shared;
                _identity = new FileDeviceIdentity(directory);
                _clock = new HybridLogicalClock(new SkewedClock(skew), _identity);
            }

            public string Directory { get; }

            public Guid Id => _identity.DeviceId;

            public EchoPlayDbContext Open() => new(new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite($"Data Source={Path.Combine(Directory, "echoplay.db")}")
                .AddInterceptors(new ChangeStampInterceptor(_clock))
                .Options);

            public async Task EditAsync(Action<EchoPlayDbContext> edit, CancellationToken ct)
            {
                await using EchoPlayDbContext context = Open();
                edit(context);
                _ = await context.SaveChangesAsync(ct);
            }

            public async Task<int> PublishAsync(CancellationToken ct)
            {
                await using EchoPlayDbContext context = Open();
                return await Exchange(context).PublishAsync(_shared, ct);
            }

            public async Task<ReceiveResult> ReceiveAsync(CancellationToken ct)
            {
                await using EchoPlayDbContext context = Open();
                return await Exchange(context).ReceiveAsync(_shared, ct);
            }

            public async Task<bool> CompactAsync(int threshold, CancellationToken ct)
            {
                await using EchoPlayDbContext context = Open();
                return await Exchange(context).CompactAsync(_shared, threshold, ct);
            }

            public async Task<List<ChangeRecord>> RecordsAsync(CancellationToken ct)
            {
                await using EchoPlayDbContext context = Open();
                return [.. (await LocalReplica.LoadAsync(context, ct)).Records];
            }

            private DeviceReplicationExchange Exchange(EchoPlayDbContext context)
                => new(context, _identity, _clock, TimeProvider.System, Loggers);
        }

        /// <summary>Eine Uhr, die um einen festen Betrag falsch geht und bei jedem Lesen eine Millisekunde weiterläuft.</summary>
        private sealed class SkewedClock(TimeSpan skew) : IClock
        {
            private long _ticks = Noon.Ticks;

            public DateTime UtcNow => new(Interlocked.Add(ref _ticks, TimeSpan.TicksPerMillisecond) + skew.Ticks, DateTimeKind.Utc);
        }
    }
}
