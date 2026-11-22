using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Replication;
using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Replication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="DataTransferService"/>: Zugangsdaten mit Kennwort hinaus, per DPAPI
    /// vorgemerkt, beim Start übernommen; ein falsches Kennwort merkt nichts vor.
    /// </summary>
    public sealed class DataTransferServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"echoplay_app_transfer_{Guid.NewGuid():N}");

        public DataTransferServiceTests() => _ = Directory.CreateDirectory(_root);

        [Fact]
        public async Task CredentialsTravelWithThePassword_AndArriveAfterTheRestart()
        {
            FakeSpotifyCredentialStore sourceStore = new();
            await sourceStore.SaveAsync("id-123", "geheim-456", TestContext.Current.CancellationToken);
            (DataTransferService source, _) = await CreateAsync("quelle", sourceStore);
            FakeSpotifyCredentialStore targetStore = new();
            (DataTransferService target, string targetDirectory) = await CreateAsync("ziel", targetStore);

            (string package, long length) = await source.ExportAsync(_root, "langes-Kennwort", progress: null, TestContext.Current.CancellationToken);
            StagedPackage staged = await target.StageAsync(package, TestContext.Current.CancellationToken);

            Assert.True(length > 0);
            Assert.False(staged.SealedCredentials.IsEmpty);
            Assert.True(target.ScheduleImport(staged, "langes-Kennwort"));
            Assert.True(File.Exists(Path.Combine(targetDirectory, PendingImport.FileName)));
            Assert.True(await DataTransferService.ApplyPendingCredentialsAsync(targetDirectory, targetStore, TestContext.Current.CancellationToken));
            Assert.Equal(("id-123", "geheim-456"), await targetStore.GetAsync(TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(targetDirectory, PendingImport.CredentialsFileName)));
        }

        [Fact]
        public async Task WrongPassword_SchedulesNothing()
        {
            FakeSpotifyCredentialStore sourceStore = new();
            await sourceStore.SaveAsync("id", "secret", TestContext.Current.CancellationToken);
            (DataTransferService source, _) = await CreateAsync("quelle", sourceStore);
            (DataTransferService target, string targetDirectory) = await CreateAsync("ziel", new FakeSpotifyCredentialStore());
            (string package, _) = await source.ExportAsync(_root, "richtiges-Kennwort", progress: null, TestContext.Current.CancellationToken);
            StagedPackage staged = await target.StageAsync(package, TestContext.Current.CancellationToken);

            Assert.False(target.ScheduleImport(staged, "falsches-Kennwort"));
            Assert.False(File.Exists(Path.Combine(targetDirectory, PendingImport.FileName)));
            Assert.False(File.Exists(Path.Combine(targetDirectory, PendingImport.CredentialsFileName)));
        }

        [Fact]
        public async Task ExportWithoutPassword_CarriesNoCredentials()
        {
            FakeSpotifyCredentialStore store = new();
            await store.SaveAsync("id", "secret", TestContext.Current.CancellationToken);
            (DataTransferService source, _) = await CreateAsync("quelle", store);
            (DataTransferService target, _) = await CreateAsync("ziel", new FakeSpotifyCredentialStore());

            (string package, _) = await source.ExportAsync(_root, credentialsPassword: null, progress: null, TestContext.Current.CancellationToken);
            StagedPackage staged = await target.StageAsync(package, TestContext.Current.CancellationToken);

            Assert.True(staged.SealedCredentials.IsEmpty);
        }

        [Fact]
        public async Task ForeignCredentialsFile_IsDiscarded()
        {
            string directory = Path.Combine(_root, "fremd");
            _ = Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, PendingImport.CredentialsFileName);
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);
            FakeSpotifyCredentialStore store = new();

            Assert.False(await DataTransferService.ApplyPendingCredentialsAsync(directory, store, TestContext.Current.CancellationToken));
            Assert.False(store.HasCredentials);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task DiscardImport_RemovesStagedFiles()
        {
            (DataTransferService service, string directory) = await CreateAsync("ziel", new FakeSpotifyCredentialStore());
            await File.WriteAllTextAsync(Path.Combine(directory, PackageStager.StagedFileName), "x", TestContext.Current.CancellationToken);
            new PendingImport(null, Guid.NewGuid()).Write(directory);

            service.DiscardImport();

            Assert.False(File.Exists(Path.Combine(directory, PackageStager.StagedFileName)));
            Assert.False(File.Exists(Path.Combine(directory, PendingImport.FileName)));
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

        /// <summary>Eine Installation in einem eigenen Ordner, mit migrierter Datenbank und den Diensten des Datenprojekts.</summary>
        private async Task<(DataTransferService Service, string Directory)> CreateAsync(string name, FakeSpotifyCredentialStore store)
        {
            string directory = Path.Combine(_root, name);
            _ = Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "echoplay.db");

            ServiceCollection services = new();
            _ = services.AddDbContext<EchoPlayDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            _ = services.AddSingleton(new DatabaseFileLocation(databasePath));
            _ = services.AddSingleton<IDeviceIdentity>(new FileDeviceIdentity(directory));
            _ = services.AddScoped<PackageExporter>();
            _ = services.AddScoped<PackageStager>();
            ServiceProvider provider = services.BuildServiceProvider();

            using (IServiceScope scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<EchoPlayDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            return (new DataTransferService(provider.GetRequiredService<IServiceScopeFactory>(), store, new DatabaseFileLocation(databasePath)), directory);
        }
    }
}
