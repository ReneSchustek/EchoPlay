using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Helpers;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="CoverImageDataService"/>.
    /// </summary>
    public sealed class CoverImageTests : DbTestBase
    {
        [Fact]
        public async Task ClearAllAsync_RemovesAllEntries()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            await service.SetCoverAsync("Series", TestIds.Indexed(1), [0xFF, 0xD8], cancellationToken: TestContext.Current.CancellationToken);
            await service.SetCoverAsync("Episode", TestIds.Indexed(2), [0x89, 0x50], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int deleted = await service.ClearAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, deleted);
            int remaining = await Context.CoverImages.IgnoreQueryFilters().CountAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, remaining);
        }

        [Fact]
        public async Task ClearAllAsync_EmptyTable_ReturnsZero()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            int deleted = await service.ClearAllAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, deleted);
        }

        [Fact]
        public async Task SetCoverAsync_InsertAndUpdate()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(3);

            // Insert
            await service.SetCoverAsync("Series", entityId, [0x01, 0x02], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? cover = await service.GetByEntityAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(cover);
            Assert.Equal(2, cover.ImageData.Length);

            // Update
            await service.SetCoverAsync("Series", entityId, [0x03, 0x04, 0x05], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? updated = await service.GetByEntityAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(3, updated!.ImageData.Length);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsTrueForExistingCover()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(4);

            await service.SetCoverAsync("Series", entityId, [0xFF, 0xD8], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            bool exists = await service.ExistsAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(exists);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsFalseForMissingCover()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            bool exists = await service.ExistsAsync("Series", TestIds.Indexed(5), cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(exists);
        }

        [Fact]
        public async Task GetUncheckedEntityIdsAsync_ReturnsUncheckedEntities()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(6);

            // Platzhalter-Eintrag ohne Bild und ohne LastChecked
            await service.SetLastCheckedAsync("Episode", entityId, DateTime.MinValue, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            DateTime threshold = TestIds.ReferenceDate.AddDays(-1);
            IReadOnlyList<Guid> pending = await service.GetUncheckedEntityIdsAsync("Episode", threshold, 10, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains(entityId, pending);
        }

        [Fact]
        public async Task GetImageDataByEntitiesAsync_ReturnsBatchResults()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid id1 = TestIds.Indexed(7);
            Guid id2 = TestIds.Indexed(8);

            await service.SetCoverAsync("Episode", id1, [0x01], cancellationToken: TestContext.Current.CancellationToken);
            await service.SetCoverAsync("Episode", id2, [0x02], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyDictionary<Guid, byte[]> result =
                await service.GetImageDataByEntitiesAsync("Episode", [id1, id2], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey(id1));
            Assert.True(result.ContainsKey(id2));
        }

        [Fact]
        public async Task GetImageDataByEntitiesAsync_FiltertPlatzhalterOhneBilddaten()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid mitCover = TestIds.Indexed(9);
            Guid platzhalter = TestIds.Indexed(10);

            // Echtes Cover
            await service.SetCoverAsync("Episode", mitCover, [0xFF, 0xD8], cancellationToken: TestContext.Current.CancellationToken);
            // Platzhalter: nur LastChecked, keine Bilddaten
            await service.SetLastCheckedAsync("Episode", platzhalter, TestIds.ReferenceDate, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            IReadOnlyDictionary<Guid, byte[]> result =
                await service.GetImageDataByEntitiesAsync("Episode", [mitCover, platzhalter], cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result);
            Assert.True(result.ContainsKey(mitCover));
            Assert.False(result.ContainsKey(platzhalter));
        }
        [Fact]
        public async Task SetCoverAsync_SetsSourceHash()
        {
            // SHA-256-Hash muss beim Speichern automatisch berechnet werden
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(11);
            byte[] imageData = [0xFF, 0xD8, 0xFF, 0xE0];

            await service.SetCoverAsync("Series", entityId, imageData, cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? cover = await service.GetByEntityAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(cover);
            Assert.NotNull(cover!.SourceHash);
            Assert.Equal(64, cover.SourceHash!.Length);

            // Hash muss zum erwarteten SHA-256 passen
            byte[] expectedHash = System.Security.Cryptography.SHA256.HashData(imageData);
            string expectedHex = Convert.ToHexString(expectedHash);
            Assert.Equal(expectedHex, cover.SourceHash);
        }

        [Fact]
        public async Task SetCoverAsync_UpdatesSourceHash_OnOverwrite()
        {
            // Bei Update muss der Hash neu berechnet werden
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(12);

            await service.SetCoverAsync("Series", entityId, [0x01, 0x02], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? first = await service.GetByEntityAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);
            string? firstHash = first!.SourceHash;

            await service.SetCoverAsync("Series", entityId, [0x03, 0x04, 0x05], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? second = await service.GetByEntityAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(firstHash, second!.SourceHash);
        }

        [Fact]
        public async Task SetCoverAsync_UniqueViolation_FallsBackToUpdate()
        {
            // Zwei Inserts für dieselbe Entity-Kombination: der zweite muss als Update durchgehen
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(13);

            await service.SetCoverAsync("Episode", entityId, [0x01], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            // Zweiter Aufruf mit anderen Daten — kein Fehler erwartet
            await service.SetCoverAsync("Episode", entityId, [0x02, 0x03], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? cover = await service.GetByEntityAsync("Episode", entityId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(cover);
            Assert.Equal(2, cover!.ImageData.Length);
            Assert.Equal(0x02, cover.ImageData[0]);
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_RemovesMatchingEntries()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid id1 = TestIds.Indexed(20);
            Guid id2 = TestIds.Indexed(21);
            Guid id3 = TestIds.Indexed(22);

            await service.SetCoverAsync("Series", id1, [0x01], cancellationToken: TestContext.Current.CancellationToken);
            await service.SetCoverAsync("Series", id2, [0x02], cancellationToken: TestContext.Current.CancellationToken);
            await service.SetCoverAsync("Series", id3, [0x03], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int deleted = await service.DeleteByEntitiesAsync("Series", [id1, id3], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, deleted);
            Assert.False(await service.ExistsAsync("Series", id1, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(await service.ExistsAsync("Series", id2, cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(await service.ExistsAsync("Series", id3, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_EmptyList_ReturnsZero()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            int deleted = await service.DeleteByEntitiesAsync("Series", [], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, deleted);
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_DoesNotTouchOtherEntityType()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid sharedId = TestIds.Indexed(23);

            await service.SetCoverAsync("Series", sharedId, [0x01], cancellationToken: TestContext.Current.CancellationToken);
            await service.SetCoverAsync("Episode", sharedId, [0x02], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            int deleted = await service.DeleteByEntitiesAsync("Series", [sharedId], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, deleted);
            Assert.False(await service.ExistsAsync("Series", sharedId, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(await service.ExistsAsync("Episode", sharedId, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_UnknownIds_ReturnsZero()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            int deleted = await service.DeleteByEntitiesAsync("Series",
                [TestIds.Indexed(900), TestIds.Indexed(901)], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, deleted);
        }

        [Fact]
        public async Task SetCoverAsync_AfterSoftDelete_AllowsReinsert()
        {
            // Ohne HasFilter("IsDeleted = 0") auf dem UNIQUE-Index würde der soft-deleted
            // Eintrag den UNIQUE-Slot blockieren und der zweite SetCover-Aufruf bricht ab.
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(50);

            await service.SetCoverAsync("Series", entityId, [0xAA, 0xBB], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? existing = await Context.CoverImages
                .IgnoreQueryFilters()
                .AsTracking()
                .FirstAsync(c => c.EntityType == "Series" && c.EntityId == entityId, cancellationToken: TestContext.Current.CancellationToken);
            existing.MarkAsDeleted(TestIds.ReferenceDate);
            _ = await Context.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            await service.SetCoverAsync("Series", entityId, [0xCC, 0xDD, 0xEE], cancellationToken: TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            CoverImage? reinserted = await service.GetByEntityAsync("Series", entityId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(reinserted);
            Assert.Equal(3, reinserted!.ImageData.Length);
            Assert.False(reinserted.IsDeleted);

            int totalIncludingDeleted = await Context.CoverImages
                .IgnoreQueryFilters()
                .CountAsync(c => c.EntityType == "Series" && c.EntityId == entityId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, totalIncludingDeleted);
        }
    }
}

