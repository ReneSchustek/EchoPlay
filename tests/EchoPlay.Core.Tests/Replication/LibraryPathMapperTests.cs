using EchoPlay.Core.Replication;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="LibraryPathMapper"/>: Sonderzeichen, <c>..</c>, andere Laufwerke, UNC.
    /// </summary>
    public sealed class LibraryPathMapperTests
    {
        private const string Root = @"D:\Mp3\Hörspiele";

        [Theory]
        [InlineData(@"D:\Mp3\Hörspiele\TKKG\001 - Die Jagd\01.mp3", "TKKG/001 - Die Jagd/01.mp3")]
        [InlineData(@"d:\mp3\HÖRSPIELE\TKKG", "TKKG")]
        [InlineData(@"D:\Mp3\Hörspiele\Die drei ???\170 – Schatz & Co. (2014)", "Die drei ???/170 – Schatz & Co. (2014)")]
        [InlineData(@"D:\Mp3\Hörspiele\A\..\B\x.mp3", "B/x.mp3")]
        public void ToPortable_InsideRoot_GivesRelativeWithSlashes(string absolute, string expected)
        {
            Assert.Equal(expected, LibraryPathMapper.ToPortable(Root, absolute));
        }

        [Theory]
        [InlineData(@"D:\Mp3\Hörspiele alt\TKKG")]
        [InlineData(@"E:\Mp3\Hörspiele\TKKG")]
        [InlineData(@"D:\Mp3\Hörspiele\..\Musik\x.mp3")]
        [InlineData(@"\\nas\Hörspiele\TKKG")]
        [InlineData(@"D:\Mp3\Hörspiele")]
        [InlineData(@"TKKG\relativ")]
        [InlineData("")]
        public void ToPortable_OutsideRootOrNotAbsolute_GivesNull(string absolute)
        {
            Assert.Null(LibraryPathMapper.ToPortable(Root, absolute));
        }

        [Fact]
        public void ToPortable_UncRoot_Works()
        {
            Assert.Equal("TKKG/01.mp3", LibraryPathMapper.ToPortable(@"\\nas\Hörspiele", @"\\nas\Hörspiele\TKKG\01.mp3"));
        }

        [Theory]
        [InlineData("../Musik/x.mp3")]
        [InlineData("TKKG/../../x.mp3")]
        [InlineData(@"C:\Windows\x.dll")]
        [InlineData("/etc/passwd")]
        [InlineData("..")]
        [InlineData("")]
        public void ToAbsolute_LeavingRootOrRooted_IsRejected(string portable)
        {
            Assert.Null(LibraryPathMapper.ToAbsolute(Root, portable));
        }

        [Fact]
        public void ToAbsolute_RoundTripsWithToPortable()
        {
            string original = @"D:\Mp3\Hörspiele\Fünf Freunde\012 - Ärger\03.mp3";

            string portable = LibraryPathMapper.ToPortable(Root, original)!;

            Assert.Equal(original, LibraryPathMapper.ToAbsolute(Root, portable));
        }

        [Fact]
        public void Rebase_MovesPathToNewRoot()
        {
            Assert.Equal(
                @"F:\Medien\Hörspiele\TKKG\01.mp3",
                LibraryPathMapper.Rebase(Root, @"F:\Medien\Hörspiele\", @"D:\Mp3\Hörspiele\TKKG\01.mp3"));
        }

        [Fact]
        public void Rebase_PathOutsideOldRoot_GivesNull()
        {
            Assert.Null(LibraryPathMapper.Rebase(Root, @"F:\Medien", @"C:\Andere\TKKG\01.mp3"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("relativ")]
        public void InvalidRoot_GivesNull(string root)
        {
            Assert.Null(LibraryPathMapper.ToPortable(root, @"D:\Mp3\x.mp3"));
            Assert.Null(LibraryPathMapper.ToAbsolute(root, "x.mp3"));
        }
    }
}
