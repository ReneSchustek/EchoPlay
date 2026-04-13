using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
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

            await service.SetCoverAsync("Series", Guid.NewGuid(), [0xFF, 0xD8]);
            await service.SetCoverAsync("Episode", Guid.NewGuid(), [0x89, 0x50]);
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
            Guid entityId = Guid.NewGuid();

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
            Guid entityId = Guid.NewGuid();

            await service.SetCoverAsync("Series", entityId, [0xFF, 0xD8]);
            Context.ChangeTracker.Clear();

            bool exists = await service.ExistsAsync("Series", entityId);

            Assert.True(exists);
        }

        [Fact]
        public async Task ExistsAsync_ReturnsFalseForMissingCover()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);

            bool exists = await service.ExistsAsync("Series", Guid.NewGuid());

            Assert.False(exists);
        }

        [Fact]
        public async Task GetUncheckedEntityIdsAsync_ReturnsUncheckedEntities()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = Guid.NewGuid();

            // Platzhalter-Eintrag ohne Bild und ohne LastChecked
            await service.SetLastCheckedAsync("Episode", entityId, DateTime.MinValue);
            Context.ChangeTracker.Clear();

            DateTime threshold = DateTime.UtcNow.AddDays(-1);
            IReadOnlyList<Guid> pending = await service.GetUncheckedEntityIdsAsync("Episode", threshold, 10);

            Assert.Contains(entityId, pending);
        }

        [Fact]
        public async Task GetImageDataByEntitiesAsync_ReturnsBatchResults()
        {
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid id1 = Guid.NewGuid();
            Guid id2 = Guid.NewGuid();

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
            Guid mitCover = Guid.NewGuid();
            Guid platzhalter = Guid.NewGuid();

            // Echtes Cover
            await service.SetCoverAsync("Episode", mitCover, [0xFF, 0xD8]);
            // Platzhalter: nur LastChecked, keine Bilddaten
            await service.SetLastCheckedAsync("Episode", platzhalter, DateTime.UtcNow);
            Context.ChangeTracker.Clear();

            IReadOnlyDictionary<Guid, byte[]> result =
                await service.GetImageDataByEntitiesAsync("Episode", [mitCover, platzhalter]);

            Assert.Single(result);
            Assert.True(result.ContainsKey(mitCover));
            Assert.False(result.ContainsKey(platzhalter));
        }
        [Fact]
        public async Task SetCoverAsync_SetsSourceHash()
        {
            // SHA-256-Hash muss beim Speichern automatisch berechnet werden
            CoverImageDataService service = new(Context, NullLoggerFactory);
            Guid entityId = Guid.NewGuid();
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
            Guid entityId = Guid.NewGuid();

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
            Guid entityId = Guid.NewGuid();

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
    }
}

