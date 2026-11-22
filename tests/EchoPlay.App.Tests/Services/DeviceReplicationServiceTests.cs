using EchoPlay.App.Services;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication.Exchange;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="DeviceReplicationService"/>: ein Durchgang mit und ohne gemeinsamen
    /// Ordner, das letzte Schreiben beim Beenden, die Schleife mit Abstand und Wecken, Fehler im Ordner.
    /// </summary>
    public sealed class DeviceReplicationServiceTests : IDisposable
    {
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_app_replication_{Guid.NewGuid():N}");
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        private ServiceProvider? _provider;

        public DeviceReplicationServiceTests() => _ = Directory.CreateDirectory(_root);

        private string Shared => Path.Combine(_root, "Gemeinsam");

        [Fact]
        public async Task RunOnce_WithReplicationOff_DoesNothing()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: false);

            Assert.Null(await service.RunOnceAsync(TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(Path.Combine(Shared, ExchangeLayout.ChangesFolder)));
        }

        [Fact]
        public async Task RunOnce_WithMissingFolder_DoesNothing()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true, createFolder: false);

            Assert.Null(await service.RunOnceAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RunOnce_PublishesOwnChanges_AndReportsTheRun()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            await AddSeriesAsync("TKKG");
            List<ReplicationRun> runs = [];
            service.RunCompleted += runs.Add;

            ReplicationRun? run = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(run);
            Assert.True(run.Published > 0);
            Assert.Null(run.Error);
            Assert.Same(run, service.LastRun);
            Assert.Same(run, Assert.Single(runs));
            _ = Assert.Single(Directory.GetFiles(Path.Combine(Shared, ExchangeLayout.ChangesFolder), "*.json", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Stop_WritesTheLastChangesEvenWithoutAStartedLoop()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            await AddSeriesAsync("Benjamin Blümchen");

            await service.StopAsync(TimeSpan.FromSeconds(30));

            _ = Assert.Single(Directory.GetFiles(Path.Combine(Shared, ExchangeLayout.ChangesFolder), "*.json", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Loop_RunsAfterTheDelay_AndAgainWhenWoken()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            int runs = 0;
            service.RunCompleted += _ => Interlocked.Increment(ref runs);

            service.Start();
            service.Start();
            await AdvanceUntilAsync(() => Volatile.Read(ref runs) >= 1);

            service.RequestRun();
            await AdvanceUntilAsync(() => Volatile.Read(ref runs) >= 2);

            // Eine neue Datei im Ordner weckt ebenfalls.
            string peer = Path.Combine(Shared, ExchangeLayout.ChangesFolder, Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(peer);
            await File.WriteAllTextAsync(Path.Combine(peer, "x.txt"), "neu", TestContext.Current.CancellationToken);
            await AdvanceUntilAsync(() => Volatile.Read(ref runs) >= 3);

            await service.StopAsync(TimeSpan.FromSeconds(30));
            Assert.True(runs >= 3);
        }

        [Fact]
        public async Task FolderThatCannotTakeTheProtocol_ReportsTheErrorWithoutStopping()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            await File.WriteAllTextAsync(Path.Combine(Shared, ExchangeLayout.ChangesFolder), "Datei statt Ordner", TestContext.Current.CancellationToken);
            await AddSeriesAsync("TKKG");

            ReplicationRun? run = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(run);
            Assert.NotNull(run.Error);

            // Auch das letzte Schreiben beim Beenden scheitert, ohne zu werfen.
            await service.StopAsync(TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task Stop_WithExpiredDeadline_GivesUpQuietly()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            await AddSeriesAsync("TKKG");

            await service.StopAsync(TimeSpan.Zero);
        }

        [Fact]
        public async Task SwitchingReplicationOff_EndsTheFolderWatch()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            _ = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            using (IServiceScope scope = _provider!.CreateScope())
            {
                EchoPlayDbContext context = scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>();
                (await context.AppSettings.SingleAsync(TestContext.Current.CancellationToken)).ReplicationEnabled = false;
                _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            Assert.Null(await service.RunOnceAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Stop_WhileTheLoopWaits_WithAnExpiredDeadline_ReturnsAtOnce()
        {
            using DeviceReplicationService service = await CreateAsync(enabled: true);
            service.Start();

            await service.StopAsync(TimeSpan.Zero);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _provider?.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        /// <summary>Lässt die künstliche Zeit laufen, bis die Bedingung erfüllt ist oder fünf echte Sekunden um sind.</summary>
        private Task AdvanceUntilAsync(Func<bool> condition)
            => Eventually.TrueAsync(condition, () => _time.Advance(TimeSpan.FromSeconds(10)));

        private async Task<DeviceReplicationService> CreateAsync(bool enabled, bool createFolder = true)
        {
            string databasePath = Path.Combine(_root, "echoplay.db");
            FileDeviceIdentity device = new(_root);
            HybridLogicalClock clock = new(new SystemClock(), device);

            ServiceCollection services = new();
            _ = services.AddDbContext<EchoPlayDbContext>(options => options
                .UseSqlite($"Data Source={databasePath}")
                .AddInterceptors(new ChangeStampInterceptor(clock)));
            _ = services.AddScoped<IAppSettingsDataService>(p => new AppSettingsDataService(p.GetRequiredService<EchoPlayDbContext>(), Loggers));
            _ = services.AddScoped(p => new DeviceReplicationExchange(p.GetRequiredService<EchoPlayDbContext>(), device, clock, TimeProvider.System, Loggers));
            _provider = services.BuildServiceProvider();

            using (IServiceScope scope = _provider.CreateScope())
            {
                EchoPlayDbContext context = scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>();
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
                _ = context.AppSettings.Add(new AppSettings { ReplicationEnabled = enabled, ReplicationFolderPath = Shared });
                _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            if (createFolder)
            {
                _ = Directory.CreateDirectory(Shared);
            }

            return new DeviceReplicationService(_provider.GetRequiredService<IServiceScopeFactory>(), Loggers, _time);
        }

        private async Task AddSeriesAsync(string title)
        {
            using IServiceScope scope = _provider!.CreateScope();
            EchoPlayDbContext context = scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>();
            _ = context.Series.Add(new Series { Title = title });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
