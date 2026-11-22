using EchoPlay.App.Services;
using EchoPlay.App.Startup;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Replication;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Startup
{
    /// <summary>
    /// Tests für <see cref="ImportStartup"/>: der Teil des Starts, der ein vorgemerktes Paket einspielt.
    /// </summary>
    public sealed class ImportStartupTests : IDisposable
    {
        private static readonly EchoPlay.Logger.Core.LoggerFactory Loggers =
            new([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_import_startup_{Guid.NewGuid():N}");

        public ImportStartupTests() => _ = Directory.CreateDirectory(_root);

        [Fact]
        public async Task NothingScheduled_ChangesNothing()
        {
            (ServiceProvider provider, _, _) = await CreateAsync("leer", new FakeSpotifyCredentialStore());

            Assert.Null(await ImportStartup.BeforeOpenAsync(provider.GetRequiredService<IServiceScopeFactory>()));
        }

        [Fact]
        public async Task ScheduledPackage_IsAppliedAroundTheMigration_AndBringsTheCredentials()
        {
            FakeSpotifyCredentialStore sourceStore = new();
            await sourceStore.SaveAsync("id-1", "geheim-1", TestContext.Current.CancellationToken);
            (ServiceProvider sourceProvider, DataTransferService source, _) = await CreateAsync("quelle", sourceStore);
            FakeSpotifyCredentialStore targetStore = new();
            (ServiceProvider targetProvider, DataTransferService target, string targetDirectory) = await CreateAsync("ziel", targetStore);

            (string package, _) = await source.ExportAsync(_root, "Kennwort123", progress: null, TestContext.Current.CancellationToken);
            StagedPackage staged = await target.StageAsync(package, TestContext.Current.CancellationToken);
            Assert.True(target.ScheduleImport(staged, "Kennwort123"));
            await sourceProvider.DisposeAsync();
            SqliteConnection.ClearAllPools();

            IServiceScopeFactory scopes = targetProvider.GetRequiredService<IServiceScopeFactory>();
            ImportCarryOver carry = (await ImportStartup.BeforeOpenAsync(scopes))!;
            using (IServiceScope scope = scopes.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            ImportFinished finished = await ImportStartup.AfterMigrationAsync(scopes, carry, Loggers.CreateLogger("Test"));

            Assert.True(finished.RestoredSettings > 0);
            Assert.Equal(("id-1", "geheim-1"), await targetStore.GetAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(targetDirectory, PendingImport.FileName)));
        }

        [Fact]
        public async Task EmptyCredentialsInThePackage_AreNotApplied()
        {
            FakeSpotifyCredentialStore sourceStore = new();
            await sourceStore.SaveAsync(" ", " ", TestContext.Current.CancellationToken);
            (_, DataTransferService source, _) = await CreateAsync("quelle", sourceStore);
            FakeSpotifyCredentialStore targetStore = new();
            (_, DataTransferService target, string targetDirectory) = await CreateAsync("ziel", targetStore);

            (string package, _) = await source.ExportAsync(_root, "Kennwort123", progress: null, TestContext.Current.CancellationToken);
            Assert.True(target.ScheduleImport(await target.StageAsync(package, TestContext.Current.CancellationToken), "Kennwort123"));

            Assert.False(await DataTransferService.ApplyPendingCredentialsAsync(targetDirectory, targetStore, TestContext.Current.CancellationToken));
            Assert.False(targetStore.HasCredentials);
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

        private async Task<(ServiceProvider Provider, DataTransferService Service, string Directory)> CreateAsync(string name, FakeSpotifyCredentialStore store)
        {
            string directory = Path.Combine(_root, name);
            _ = Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "echoplay.db");

            ServiceCollection services = new();
            _ = services.AddDbContext<EchoPlayDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            _ = services.AddSingleton(new DatabaseFileLocation(databasePath));
            _ = services.AddSingleton<IDeviceIdentity>(new FileDeviceIdentity(directory));
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(Loggers);
            _ = services.AddSingleton<ISpotifyCredentialStore>(store);
            _ = services.AddScoped<ILibraryRootRebaser, LibraryRootRebaser>();
            _ = services.AddScoped<ISecureSettingsDataService, SecureSettingsDataService>();
            _ = services.AddScoped<PackageExporter>();
            _ = services.AddScoped<PackageStager>();
            _ = services.AddScoped<ImportFinisher>();
            ServiceProvider provider = services.BuildServiceProvider();

            using (IServiceScope scope = provider.CreateScope())
            {
                EchoPlayDbContext context = scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>();
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
                _ = context.AppSettings.Add(new AppSettings { LocalLibraryRootPath = Path.Combine(directory, "medien") });
                _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            return (provider, new DataTransferService(provider.GetRequiredService<IServiceScopeFactory>(), store, new DatabaseFileLocation(databasePath)), directory);
        }
    }
}
