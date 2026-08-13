using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Tests.Fakes;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Metadata
{
    /// <summary>
    /// Prüft, dass die Trackliste den Titel aus der Kennzeichnung bekommt und in jedem
    /// Störfall auf den Dateinamen zurückfällt — nie auf eine leere Zeile.
    /// </summary>
    public sealed class TrackTitleResolverTests
    {
        private const string FirstTrack = @"C:\Audio\Serie\Folge 1\01 - Das leere Haus (Teil 1).mp3";
        private const string SecondTrack = @"C:\Audio\Serie\Folge 1\02 - Das leere Haus (Teil 2).mp3";

        [Fact]
        public async Task ResolveAsync_TagTitleIsSet_ReturnsTagTitle()
        {
            FakeTagTitleReader reader = new(new Dictionary<string, (string, string)>
            {
                [FirstTrack] = ("Das leere Haus (Teil 1)", "Sherlock Holmes"),
                [SecondTrack] = ("Das leere Haus (Teil 2)", "Sherlock Holmes"),
            });

            TrackTitleResolver resolver = new(new FakeLoggerFactory(), reader);

            IReadOnlyList<string> titles = await resolver.ResolveAsync(
                [new TrackTitleRequest(FirstTrack, 1), new TrackTitleRequest(SecondTrack, 2)],
                TestContext.Current.CancellationToken);

            Assert.Equal(["Das leere Haus (Teil 1)", "Das leere Haus (Teil 2)"], titles);
        }

        [Fact]
        public async Task ResolveAsync_TagTitleIsMissing_ReturnsCleanedFileName()
        {
            // Ungepflegte Sammlung: Der Dateiname bleibt, aber ohne Endung und ohne die
            // Nummer, die links daneben schon steht.
            TrackTitleResolver resolver = new(new FakeLoggerFactory(), new FakeTagTitleReader());

            IReadOnlyList<string> titles = await resolver.ResolveAsync(
                [new TrackTitleRequest(FirstTrack, 1)],
                TestContext.Current.CancellationToken);

            Assert.Equal(["Das leere Haus (Teil 1)"], titles);
        }

        [Fact]
        public async Task ResolveAsync_TagTitleIsAPlaceholder_ReturnsCleanedFileName()
        {
            FakeTagTitleReader reader = new(new Dictionary<string, (string, string)>
            {
                [FirstTrack] = ("Track 01", string.Empty),
            });

            TrackTitleResolver resolver = new(new FakeLoggerFactory(), reader);

            IReadOnlyList<string> titles = await resolver.ResolveAsync(
                [new TrackTitleRequest(FirstTrack, 1)],
                TestContext.Current.CancellationToken);

            Assert.Equal(["Das leere Haus (Teil 1)"], titles);
        }

        [Fact]
        public async Task ResolveAsync_FileCannotBeRead_ReturnsCleanedFileName()
        {
            // Datei gesperrt oder defekt: Die Zeile darf trotzdem nicht leer bleiben.
            TrackTitleResolver resolver = new(new FakeLoggerFactory(), new ThrowingTagTitleReader());

            IReadOnlyList<string> titles = await resolver.ResolveAsync(
                [new TrackTitleRequest(FirstTrack, 1)],
                TestContext.Current.CancellationToken);

            Assert.Equal(["Das leere Haus (Teil 1)"], titles);
        }

        [Fact]
        public async Task ResolveAsync_NoTracks_ReturnsEmptyList()
        {
            TrackTitleResolver resolver = new(new FakeLoggerFactory(), new FakeTagTitleReader());

            IReadOnlyList<string> titles = await resolver.ResolveAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(titles);
        }

        /// <summary>Tag-Leser, der jede Datei als unlesbar meldet.</summary>
        private sealed class ThrowingTagTitleReader : ITagTitleReader
        {
            public (string Title, string Album) Read(string filePath)
            {
                throw new IOException("Datei gesperrt");
            }
        }
    }
}
