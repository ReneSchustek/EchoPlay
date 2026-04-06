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
    }
}
