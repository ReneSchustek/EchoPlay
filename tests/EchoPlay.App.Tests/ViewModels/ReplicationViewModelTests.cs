using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models;
using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication.Exchange;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für den Bereich „Abgleich zwischen Geräten“ (<see cref="ReplicationSettingsViewModel"/>)
    /// und den Hinweis in der Statusleiste (<see cref="ReplicationStatusViewModel"/>), gegen eine echte
    /// Datenbank im Temp-Ordner.
    /// </summary>
    public sealed class ReplicationViewModelTests : IDisposable
    {
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private static readonly FakeLocalizationService Texts = new(new Dictionary<string, string>
        {
            ["ReplicationOwnDeviceFormat"] = "Dieses Gerät: {0}",
            ["ReplicationLastRunFormat"] = "Zuletzt um {0}: {1} übernommen, {2} geschrieben.",
            ["ReplicationLastSeenFormat"] = "Zuletzt gelesen am {0}",
            ["ReplicationAppliedFormat"] = "{0} Sätze übernommen",
            ["ReplicationRemoveMessage"] = "Gerät {0} entfernen?",
        });

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_app_replication_vm_{Guid.NewGuid():N}");
        private readonly FileDeviceIdentity _device;
        private ServiceProvider? _provider;

        public ReplicationViewModelTests()
        {
            _ = Directory.CreateDirectory(_root);
            _device = new FileDeviceIdentity(_root);
        }

        private string Shared => Path.Combine(_root, "Gemeinsam");

        [Fact]
        public async Task Load_WithoutFolder_CannotEnable_AndListsNoDevices()
        {
            (ReplicationSettingsViewModel vm, _) = await CreateAsync();

            await vm.LoadAsync();

            Assert.Equal("ReplicationNoFolder", vm.FolderText);
            Assert.False(vm.CanEnable);
            Assert.False(vm.CanRunNow);
            Assert.Equal(Visibility.Visible, vm.NoDevicesVisibility);
            Assert.Equal(Visibility.Collapsed, vm.DevicesVisibility);
            Assert.Equal("ReplicationNoRunYet", vm.LastRunText);
            Assert.StartsWith("Dieses Gerät: ", vm.OwnDeviceText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EnableWithoutFolder_StaysOff()
        {
            (ReplicationSettingsViewModel vm, _) = await CreateAsync();
            await vm.LoadAsync();

            await vm.SetEnabledAsync(true);

            Assert.False(vm.IsEnabled);
            Assert.False(await StoredEnabledAsync());
        }

        [Fact]
        public async Task FolderAndSwitch_AreSaved_AndRunNowReportsTheRun()
        {
            (ReplicationSettingsViewModel vm, _) = await CreateAsync();
            _ = Directory.CreateDirectory(Shared);
            await vm.LoadAsync();

            await vm.SetFolderAsync(Shared);
            await vm.SetEnabledAsync(true);
            await vm.RunNowAsync();

            Assert.Equal(Shared, vm.FolderText);
            Assert.True(vm.IsEnabled);
            Assert.True(vm.CanRunNow);
            Assert.True(await StoredEnabledAsync());
            Assert.StartsWith("Zuletzt um", vm.LastRunText, StringComparison.Ordinal);
            Assert.Equal(Visibility.Collapsed, vm.ErrorVisibility);

            await vm.SetEnabledAsync(false);
            Assert.False(await StoredEnabledAsync());
        }

        [Fact]
        public async Task RunNow_WithUnreachableFolder_SaysSo()
        {
            (ReplicationSettingsViewModel vm, _) = await CreateAsync();
            await vm.LoadAsync();
            await vm.SetFolderAsync(Path.Combine(_root, "gibt-es-nicht"));
            await vm.SetEnabledAsync(true);

            await vm.RunNowAsync();

            Assert.Equal("ReplicationFolderUnreachable", vm.ErrorText);
            Assert.Equal(Visibility.Visible, vm.ErrorVisibility);
        }

        [Fact]
        public async Task Devices_ListOtherDevicesWithTheirState_AndRemoveAsksFirst()
        {
            FakeConfirmationDialogService declines = new(result: false);
            (ReplicationSettingsViewModel vm, _) = await CreateAsync(declines);
            Guid other = Guid.NewGuid();
            Guid silent = Guid.NewGuid();
            await AddPeersAsync(
                new ReplicationPeer { DeviceId = _device.DeviceId, LastSequence = 3 },
                new ReplicationPeer { DeviceId = other, AppliedRecords = 12, LastSeenUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc), LastError = "Datei 4: abgeschnitten" },
                new ReplicationPeer { DeviceId = silent });

            await vm.LoadAsync();

            Assert.Equal(2, vm.Devices.Count);
            ReplicationDeviceItemViewModel first = vm.Devices[0];
            Assert.Equal(other.ToString("N")[..8], first.Name);
            Assert.Equal("12 Sätze übernommen", first.AppliedText);
            Assert.StartsWith("Zuletzt gelesen am", first.LastSeenText, StringComparison.Ordinal);
            Assert.Equal(Visibility.Visible, first.ErrorVisibility);
            Assert.Equal("ReplicationNeverSeen", vm.Devices[1].LastSeenText);
            Assert.Equal(Visibility.Collapsed, vm.Devices[1].ErrorVisibility);

            await vm.RemoveDeviceAsync(other);
            Assert.Equal(DialogKey.ReplicationDeviceRemove, declines.LastKey);
            Assert.Equal(2, vm.Devices.Count);
        }

        [Fact]
        public async Task RemoveConfirmed_TakesTheDeviceOffTheList()
        {
            (ReplicationSettingsViewModel vm, _) = await CreateAsync(new FakeConfirmationDialogService(result: true));
            Guid other = Guid.NewGuid();
            await AddPeersAsync(new ReplicationPeer { DeviceId = other });
            await vm.LoadAsync();

            vm.Devices.Single().RemoveCommand.Execute(null);
            await WaitUntilAsync(() => vm.Devices.Count == 0);

            Assert.Equal(Visibility.Visible, vm.NoDevicesVisibility);
            using IServiceScope scope = _provider!.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<IReplicationPeerDataService>().RemoveAsync(other, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task StatusHint_FollowsTheService()
        {
            (_, DeviceReplicationService service) = await CreateAsync();
            ReplicationStatusViewModel status = new(service, Texts, dispatcher: null);
            Assert.Equal(Visibility.Collapsed, status.HintVisibility);

            // Wo das Protokoll hin soll, liegt eine Datei: Das Schreiben scheitert.
            _ = Directory.CreateDirectory(Shared);
            await SetSettingsAsync(Shared, enabled: true);
            await File.WriteAllTextAsync(Path.Combine(Shared, ExchangeLayout.ChangesFolder), "Datei statt Ordner", TestContext.Current.CancellationToken);
            await AddPeersAsync();

            ReplicationRun? run = await service.RunOnceAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(run?.Error);
            Assert.Equal("ReplicationStatusProblem", status.HintText);
            Assert.Equal(run!.Error, status.DetailText);
            Assert.Equal(Visibility.Visible, status.HintVisibility);
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

        private static Task WaitUntilAsync(Func<bool> condition) => Eventually.TrueAsync(condition);

        private async Task<(ReplicationSettingsViewModel ViewModel, DeviceReplicationService Service)> CreateAsync(FakeConfirmationDialogService? confirm = null)
        {
            string databasePath = Path.Combine(_root, "echoplay.db");
            HybridLogicalClock clock = new(new SystemClock(), _device);
            ServiceCollection services = new();
            _ = services.AddDbContext<EchoPlayDbContext>(options => options
                .UseSqlite($"Data Source={databasePath}")
                .AddInterceptors(new ChangeStampInterceptor(clock)));
            _ = services.AddScoped<IAppSettingsDataService>(p => new AppSettingsDataService(p.GetRequiredService<EchoPlayDbContext>(), Loggers));
            _ = services.AddScoped<IReplicationPeerDataService>(p => new ReplicationPeerDataService(p.GetRequiredService<EchoPlayDbContext>()));
            _ = services.AddScoped(p => new DeviceReplicationExchange(p.GetRequiredService<EchoPlayDbContext>(), _device, clock, TimeProvider.System, Loggers));
            _provider = services.BuildServiceProvider();

            using (IServiceScope scope = _provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            IServiceScopeFactory scopes = _provider.GetRequiredService<IServiceScopeFactory>();
            DeviceReplicationService service = new(scopes, Loggers);
            ReplicationSettingsViewModel vm = new(scopes, service, _device, confirm ?? new FakeConfirmationDialogService(), Texts, Loggers);
            return (vm, service);
        }

        private async Task<bool> StoredEnabledAsync()
        {
            using IServiceScope scope = _provider!.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>().GetAsync(TestContext.Current.CancellationToken)).ReplicationEnabled;
        }

        private async Task SetSettingsAsync(string folder, bool enabled)
        {
            using IServiceScope scope = _provider!.CreateScope();
            IAppSettingsDataService settings = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings current = await settings.GetAsync(TestContext.Current.CancellationToken);
            current.ReplicationFolderPath = folder;
            current.ReplicationEnabled = enabled;
            await settings.SaveAsync(current, TestContext.Current.CancellationToken);
        }

        private async Task AddPeersAsync(params ReplicationPeer[] peers)
        {
            using IServiceScope scope = _provider!.CreateScope();
            EchoPlayDbContext context = scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>();
            context.ReplicationPeers.AddRange(peers);
            _ = context.Series.Add(new EchoPlay.Data.Entities.Library.Series { Title = "TKKG" });
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
