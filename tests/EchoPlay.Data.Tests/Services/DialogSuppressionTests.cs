using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für die dauerhaft ausgeblendeten Dialoge. Der interessante Teil ist der
    /// gefilterte UNIQUE-Index: Ein zurückgeholter Hinweis muss sich erneut ausblenden
    /// lassen, obwohl die alte Zeile logisch gelöscht noch in der Tabelle steht.
    /// </summary>
    public sealed class DialogSuppressionTests : DbTestBase
    {
        private DialogSuppressionDataService CreateService() => new(Context, NullLoggerFactory);

        [Fact]
        public async Task SuppressAsync_CreatesRow()
        {
            DialogSuppressionDataService service = CreateService();

            await service.SuppressAsync("SeriesRemoveFromLibrary", TestContext.Current.CancellationToken);

            IReadOnlyList<DialogSuppression> all =
                await service.GetAllAsync(TestContext.Current.CancellationToken);

            DialogSuppression only = Assert.Single(all);
            Assert.Equal("SeriesRemoveFromLibrary", only.Key);
        }

        [Fact]
        public async Task SuppressAsync_SameKeyTwice_StaysOneRow()
        {
            DialogSuppressionDataService service = CreateService();

            await service.SuppressAsync("ImportFailed", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();
            await service.SuppressAsync("ImportFailed", TestContext.Current.CancellationToken);

            IReadOnlyList<DialogSuppression> all =
                await service.GetAllAsync(TestContext.Current.CancellationToken);

            _ = Assert.Single(all);
        }

        [Fact]
        public async Task SuppressAsync_EmptyKey_CreatesNothing()
        {
            DialogSuppressionDataService service = CreateService();

            await service.SuppressAsync("   ", TestContext.Current.CancellationToken);

            Assert.Empty(await service.GetAllAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RestoreAsync_RemovesRowFromTheList()
        {
            DialogSuppressionDataService service = CreateService();
            await service.SuppressAsync("OnlineSearchFailed", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await service.RestoreAsync("OnlineSearchFailed", TestContext.Current.CancellationToken);

            Assert.Empty(await service.GetAllAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RestoreAsync_UnknownKey_DoesNothing()
        {
            DialogSuppressionDataService service = CreateService();
            await service.SuppressAsync("ImportFailed", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await service.RestoreAsync("GibtEsNicht", TestContext.Current.CancellationToken);

            _ = Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SuppressAgainAfterRestore_Succeeds()
        {
            // Genau dafür ist der UNIQUE-Index auf aktive Zeilen gefiltert. Ohne den Filter
            // liefe dieser zweite Versuch in einen Konflikt mit der soft-gelöschten Zeile.
            DialogSuppressionDataService service = CreateService();
            await service.SuppressAsync("LibraryReinit", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();
            await service.RestoreAsync("LibraryReinit", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await service.SuppressAsync("LibraryReinit", TestContext.Current.CancellationToken);

            IReadOnlyList<DialogSuppression> all =
                await service.GetAllAsync(TestContext.Current.CancellationToken);

            _ = Assert.Single(all);
        }

        [Fact]
        public async Task RestoreAllAsync_ClearsTheListAndReportsTheCount()
        {
            DialogSuppressionDataService service = CreateService();
            await service.SuppressAsync("ImportFailed", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();
            await service.SuppressAsync("LibraryScanFailed", TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int restored = await service.RestoreAllAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, restored);
            Assert.Empty(await service.GetAllAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RestoreAllAsync_WithoutEntries_ReportsZero()
        {
            DialogSuppressionDataService service = CreateService();

            Assert.Equal(0, await service.RestoreAllAsync(TestContext.Current.CancellationToken));
        }
    }
}
