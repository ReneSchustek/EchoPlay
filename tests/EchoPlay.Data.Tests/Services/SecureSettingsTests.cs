using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Integrationstests für <see cref="SecureSettingsDataService"/> gegen eine SQLite-
    /// In-Memory-Datenbank. Die Tests prüfen den Roundtrip verschlüsselter Byte-Arrays,
    /// das Update-Verhalten, das Löschen und das Lesen nicht existierender Keys.
    /// </summary>
    public sealed class SecureSettingsTests : DbTestBase
    {
        private readonly SecureSettingsDataService _service;

        /// <summary>Initialisiert einen neuen Test-Kontext mit frischem SQLite-In-Memory-Store.</summary>
        public SecureSettingsTests()
        {
            _service = new SecureSettingsDataService(Context, NullLoggerFactory);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenKeyDoesNotExist()
        {
            byte[]? result = await _service.GetAsync("missing", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveAsync_Persists_And_GetAsync_Returns_SameBytes()
        {
            byte[] payload = [0x01, 0x02, 0x03, 0xFE];

            await _service.SaveAsync("Spotify:ClientId", payload, cancellationToken: TestContext.Current.CancellationToken);

            byte[]? loaded = await _service.GetAsync("Spotify:ClientId", cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(payload, loaded);
        }

        [Fact]
        public async Task SaveAsync_Overwrites_Existing_Value_On_Second_Save()
        {
            await _service.SaveAsync("Spotify:Secret", [0x01], cancellationToken: TestContext.Current.CancellationToken);
            await _service.SaveAsync("Spotify:Secret", [0x02, 0x03], cancellationToken: TestContext.Current.CancellationToken);

            byte[]? loaded = await _service.GetAsync("Spotify:Secret", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal([0x02, 0x03], loaded);
        }

        [Fact]
        public async Task DeleteAsync_Removes_Existing_Entry()
        {
            await _service.SaveAsync("TempKey", [0xAA], cancellationToken: TestContext.Current.CancellationToken);

            await _service.DeleteAsync("TempKey", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(await _service.GetAsync("TempKey", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DeleteAsync_Is_NoOp_When_Key_Missing()
        {
            // Darf nicht werfen, auch wenn der Key nie existiert hat.
            await _service.DeleteAsync("never-existed", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(await _service.GetAsync("never-existed", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Multiple_Keys_Are_Isolated()
        {
            await _service.SaveAsync("A", [0x01], cancellationToken: TestContext.Current.CancellationToken);
            await _service.SaveAsync("B", [0x02], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal([0x01], await _service.GetAsync("A", cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal([0x02], await _service.GetAsync("B", cancellationToken: TestContext.Current.CancellationToken));
        }
    }
}
