using System.Reflection;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Contracts;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Jede gespeicherte Eigenschaft trägt eine <see cref="DataScope"/>; eine neue Spalte ohne
    /// Einteilung lässt diesen Test scheitern, bevor sie in Export oder Abgleich gerät.
    /// </summary>
    public sealed class DataScopeCoverageTests : IDisposable
    {
        private readonly EchoPlayDbContext _context = SqliteInMemoryDbContextFactory.Create();

        [Fact]
        public void EveryStoredProperty_HasADataScope()
        {
            List<string> missing = [.. _context.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties().Select(property => (entity, property)))
                .Where(pair => pair.property.PropertyInfo is not null)
                .Where(pair => pair.property.PropertyInfo!.GetCustomAttribute<DataScopeAttribute>() is null)
                .Select(pair => $"{pair.entity.ClrType.Name}.{pair.property.Name}")];

            Assert.Empty(missing);
        }

        [Fact]
        public void NoStoredProperty_IsShadowOnly()
        {
            // Eine Schatteneigenschaft hat kein Feld, an dem die Einteilung stehen könnte.
            List<string> shadow = [.. _context.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties()
                    .Where(property => property.IsShadowProperty())
                    .Select(property => $"{entity.ClrType.Name}.{property.Name}"))];

            Assert.Empty(shadow);
        }

        [Theory]
        [InlineData(typeof(EchoPlay.Data.Entities.Library.Episode), nameof(EchoPlay.Data.Entities.Library.Episode.LocalFolderPath), DataScope.DeviceBound)]
        [InlineData(typeof(EchoPlay.Data.Entities.Library.Series), nameof(EchoPlay.Data.Entities.Library.Series.IsFavorite), DataScope.Shared)]
        [InlineData(typeof(EchoPlay.Data.Entities.Settings.AppSettings), nameof(EchoPlay.Data.Entities.Settings.AppSettings.Volume), DataScope.DeviceBound)]
        [InlineData(typeof(EchoPlay.Data.Entities.Settings.AppSettings), nameof(EchoPlay.Data.Entities.Settings.AppSettings.OnlineEpisodeSortIndex), DataScope.Shared)]
        [InlineData(typeof(EchoPlay.Data.Entities.Library.CoverImage), nameof(EchoPlay.Data.Entities.Library.CoverImage.ImageData), DataScope.Cache)]
        [InlineData(typeof(EchoPlay.Data.Entities.Playback.PlaybackState), nameof(EchoPlay.Data.Entities.Playback.PlaybackState.EpisodeId), DataScope.LocalIdentity)]
        public void Scope_MatchesTheSplitInTheBrief(Type entity, string property, DataScope expected)
        {
            ArgumentNullException.ThrowIfNull(entity);

            DataScope actual = entity.GetProperty(property)!.GetCustomAttribute<DataScopeAttribute>()!.Scope;

            Assert.Equal(expected, actual);
        }

        /// <inheritdoc/>
        public void Dispose() => _context.Dispose();
    }
}
