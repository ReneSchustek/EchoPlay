using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="DatabaseMaintenanceService"/> – Schwerpunkt:
    /// Der Bibliotheks-Reset (alle drei Scopes) darf weder <see cref="AppSettings"/>
    /// (insb. <c>ActiveProvider</c>) noch die <c>SecureSettings</c> (Spotify-Credentials)
    /// anfassen. Nur Bibliothek-Entitäten werden geleert.
    /// </summary>
    public sealed class DatabaseMaintenanceServiceTests : DbTestBase
    {
        private async Task SeedSettingsAndLibraryAsync()
        {
            _ = Context.AppSettings.Add(new AppSettings { ActiveProvider = ProviderType.Spotify });
            _ = Context.SecureSettings.Add(new SecureSetting { Key = "Spotify:ClientId", EncryptedValue = [1, 2, 3] });
            _ = Context.SecureSettings.Add(new SecureSetting { Key = "Spotify:ClientSecret", EncryptedValue = [4, 5, 6] });

            // Etwas Bibliotheksinhalt, damit die Clear-Operationen tatsächlich Zeilen berühren.
            _ = Context.Series.Add(new Series { Title = "Online-Serie", IsOnlineImported = true });
            _ = Context.Series.Add(new Series { Title = "Lokal-Serie", LocalFolderPath = @"C:\Serie" });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();
        }

        private async Task AssertSettingsAndCredentialsUntouchedAsync()
        {
            AppSettings settings = await Context.AppSettings.IgnoreQueryFilters()
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ProviderType.Spotify, settings.ActiveProvider);

            int secureCount = await Context.SecureSettings.IgnoreQueryFilters()
                .CountAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, secureCount);
        }

        [Fact]
        public async Task ClearLibraryAsync_LeavesAppSettingsAndSecureSettingsUntouched()
        {
            await SeedSettingsAndLibraryAsync();
            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);

            await service.ClearLibraryAsync();

            await AssertSettingsAndCredentialsUntouchedAsync();
        }

        [Fact]
        public async Task ClearOnlineLibraryAsync_LeavesAppSettingsAndSecureSettingsUntouched()
        {
            await SeedSettingsAndLibraryAsync();
            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);

            await service.ClearOnlineLibraryAsync();

            await AssertSettingsAndCredentialsUntouchedAsync();
        }

        [Fact]
        public async Task ClearLocalLibraryAsync_LeavesAppSettingsAndSecureSettingsUntouched()
        {
            await SeedSettingsAndLibraryAsync();
            DatabaseMaintenanceService service = new(Context, NullLoggerFactory);

            await service.ClearLocalLibraryAsync();

            await AssertSettingsAndCredentialsUntouchedAsync();
        }
    }
}
