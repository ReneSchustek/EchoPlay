using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="LibraryRootRebaser"/>.
    /// </summary>
    public sealed class LibraryRootRebaserTests : DbTestBase
    {
        private const string OldRoot = @"D:\Mp3\Hörspiele";
        private const string NewRoot = @"F:\Medien\Hörspiele";

        [Fact]
        public async Task Rebase_MovesFoldersAndTracks_AndCountsThoseOutside()
        {
            Series series = new() { Title = "TKKG", LocalFolderPath = OldRoot + @"\TKKG" };
            Episode inside = new() { Series = series, Title = "Folge 1", LocalFolderPath = OldRoot + @"\TKKG\001" };
            Episode outside = new() { Series = series, Title = "Folge 2", LocalFolderPath = @"C:\Andere\002" };
            LocalTrack track = new() { Episode = inside, FilePath = OldRoot + @"\TKKG\001\01.mp3", TrackNumber = 1 };
            _ = Context.Series.Add(series);
            Context.Episodes.AddRange(inside, outside);
            _ = Context.LocalTracks.Add(track);
            _ = Context.AppSettings.Add(new AppSettings { LocalLibraryRootPath = OldRoot });
            _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
            Context.ChangeTracker.Clear();

            LibraryRebaseResult result = await new LibraryRootRebaser(Context, NullLoggerFactory)
                .RebaseAsync(OldRoot, NewRoot, TestContext.Current.CancellationToken);

            Assert.Equal(new LibraryRebaseResult(Rebased: 3, OutsideRoot: 1), result);
            Assert.Equal(NewRoot + @"\TKKG", (await Context.Series.SingleAsync(TestContext.Current.CancellationToken)).LocalFolderPath);
            Assert.Equal(NewRoot + @"\TKKG\001\01.mp3", (await Context.LocalTracks.SingleAsync(TestContext.Current.CancellationToken)).FilePath);
            Assert.Equal(@"C:\Andere\002", (await Context.Episodes.SingleAsync(e => e.Title == "Folge 2", TestContext.Current.CancellationToken)).LocalFolderPath);
            Assert.Equal(NewRoot, (await Context.AppSettings.SingleAsync(TestContext.Current.CancellationToken)).LocalLibraryRootPath);
        }

        [Fact]
        public async Task Rebase_RelativeRoot_IsRejected()
        {
            LibraryRootRebaser rebaser = new(Context, NullLoggerFactory);

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => rebaser.RebaseAsync(OldRoot, "relativ", TestContext.Current.CancellationToken));
        }
    }
}
