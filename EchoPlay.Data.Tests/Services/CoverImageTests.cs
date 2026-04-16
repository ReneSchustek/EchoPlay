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

            await service.SetCoverAsync("Series", TestIds.Indexed(1), [0xFF, 0xD8]);
            await service.SetCoverAsync("Episode", TestIds.Indexed(2), [0x89, 0x50]);
            Context.ChangeTracker.Clear();

            int deleted = await service.ClearAllAsync();

            Assert.Equal(2, deleted);
            int remaining = await Context.CoverImages.IgnoreQueryFilters().CountAsync();
            Assert.Equal(0, remaining);
        }

        [Fact]
        public async Task ClearAllAsync_EmptyTable_ReturnsZero()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            int deleted = await service.ClearAllAsync();

            Assert.Equal(0, deleted);
        }

        [Fact]
        public async Task SetCoverAsync_InsertAndUpdate()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(3);

            // Insert
            await service.SetCoverAsync("Series", entityId, [0x01, 0x02]);
            Context.ChangeTracker.Clear();

            CoverImage? cover = await service.GetByEntityAsync("Series", entityId);
            Assert.NotNull(cover);
            Assert.Equal(2, cover.ImageData.Length);

            // Update
            await service.SetCoverAsync("Series", entityId, [0x03, 0x04, 0x05]);
            Context.ChangeTracker.Clear();

            CoverImage? updated = await service.GetByEntityAsync("Series", entityId);
            Assert.Equal(3, updated!.ImageData.Length);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsTrueForExistingCover()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(4);

            await service.SetCoverAsync("Series", entityId, [0xFF, 0xD8]);
            Context.ChangeTracker.Clear();

            bool exists = await service.ExistsAsync("Series", entityId);

            Assert.True(exists);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsFalseForMissingCover()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            bool exists = await service.ExistsAsync("Series", TestIds.Indexed(5));

            Assert.False(exists);
        }

        [Fact]
        public async Task GetUncheckedEntityIdsAsync_ReturnsUncheckedEntities()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(6);

            // Platzhalter-Eintrag ohne Bild und ohne LastChecked
            await service.SetLastCheckedAsync("Episode", entityId, DateTime.MinValue);
            Context.ChangeTracker.Clear();

            DateTime threshold = TestIds.ReferenceDate.AddDays(-1);
            IReadOnlyList<Guid> pending = await service.GetUncheckedEntityIdsAsync("Episode", threshold, 10);

            Assert.Contains(entityId, pending);
        }

        [Fact]
        public async Task GetImageDataByEntitiesAsync_ReturnsBatchResults()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid id1 = TestIds.Indexed(7);
            Guid id2 = TestIds.Indexed(8);

            await service.SetCoverAsync("Episode", id1, [0x01]);
            await service.SetCoverAsync("Episode", id2, [0x02]);
            Context.ChangeTracker.Clear();

            IReadOnlyDictionary<Guid, byte[]> result =
                await service.GetImageDataByEntitiesAsync("Episode", [id1, id2]);

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
            await service.SetCoverAsync("Episode", mitCover, [0xFF, 0xD8]);
            // Platzhalter: nur LastChecked, keine Bilddaten
            await service.SetLastCheckedAsync("Episode", platzhalter, TestIds.ReferenceDate);
            Context.ChangeTracker.Clear();

            IReadOnlyDictionary<Guid, byte[]> result =
                await service.GetImageDataByEntitiesAsync("Episode", [mitCover, platzhalter]);

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

            await service.SetCoverAsync("Series", entityId, imageData);
            Context.ChangeTracker.Clear();

            CoverImage? cover = await service.GetByEntityAsync("Series", entityId);

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

            await service.SetCoverAsync("Series", entityId, [0x01, 0x02]);
            Context.ChangeTracker.Clear();

            CoverImage? first = await service.GetByEntityAsync("Series", entityId);
            string? firstHash = first!.SourceHash;

            await service.SetCoverAsync("Series", entityId, [0x03, 0x04, 0x05]);
            Context.ChangeTracker.Clear();

            CoverImage? second = await service.GetByEntityAsync("Series", entityId);

            Assert.NotEqual(firstHash, second!.SourceHash);
        }

        [Fact]
        public async Task SetCoverAsync_UniqueViolation_FallsBackToUpdate()
        {
            // Zwei Inserts für dieselbe Entity-Kombination: der zweite muss als Update durchgehen
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = TestIds.Indexed(13);

            await service.SetCoverAsync("Episode", entityId, [0x01]);
            Context.ChangeTracker.Clear();

            // Zweiter Aufruf mit anderen Daten — kein Fehler erwartet
            await service.SetCoverAsync("Episode", entityId, [0x02, 0x03]);
            Context.ChangeTracker.Clear();

            CoverImage? cover = await service.GetByEntityAsync("Episode", entityId);
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

            await service.SetCoverAsync("Series", id1, [0x01]);
            await service.SetCoverAsync("Series", id2, [0x02]);
            await service.SetCoverAsync("Series", id3, [0x03]);
            Context.ChangeTracker.Clear();

            int deleted = await service.DeleteByEntitiesAsync("Series", [id1, id3]);

            Assert.Equal(2, deleted);
            Assert.False(await service.ExistsAsync("Series", id1));
            Assert.True(await service.ExistsAsync("Series", id2));
            Assert.False(await service.ExistsAsync("Series", id3));
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_EmptyList_ReturnsZero()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            int deleted = await service.DeleteByEntitiesAsync("Series", []);

            Assert.Equal(0, deleted);
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_DoesNotTouchOtherEntityType()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid sharedId = TestIds.Indexed(23);

            await service.SetCoverAsync("Series", sharedId, [0x01]);
            await service.SetCoverAsync("Episode", sharedId, [0x02]);
            Context.ChangeTracker.Clear();

            int deleted = await service.DeleteByEntitiesAsync("Series", [sharedId]);

            Assert.Equal(1, deleted);
            Assert.False(await service.ExistsAsync("Series", sharedId));
            Assert.True(await service.ExistsAsync("Episode", sharedId));
        }

        [Fact]
        public async Task DeleteByEntitiesAsync_UnknownIds_ReturnsZero()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            int deleted = await service.DeleteByEntitiesAsync("Series",
                [TestIds.Indexed(900), TestIds.Indexed(901)]);

            Assert.Equal(0, deleted);
        }
    }
}

