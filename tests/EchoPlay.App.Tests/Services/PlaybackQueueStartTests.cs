using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Wahl der ersten Spur in <see cref="PlaybackQueue"/>, solange die Liste noch nicht
    /// geöffnet ist. Ohne Player öffnet sie nie; das ist genau der Zustand direkt nach dem Befüllen.
    /// </summary>
    public sealed class PlaybackQueueStartTests : IDisposable
    {
        private readonly string _directory;

        /// <summary>Legt je Test ein eigenes Verzeichnis für die leeren Spurdateien an.</summary>
        public PlaybackQueueStartTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "EchoPlayQueueStartTests_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void StartAt_OnAListNotYetOpened_KeepsTheChosenTrackAsStart()
        {
            using PlaybackQueue queue = new();
            queue.Load(CreateTracks("a1.mp3", "a2.mp3", "a3.mp3"));

            bool moved = queue.StartAt(1);

            Assert.False(moved);
            Assert.Same(queue.List.Items[1], queue.List.StartingItem);
        }

        [Fact]
        public void StartAt_WithNegativeIndex_StartsWithTheFirstTrack()
        {
            using PlaybackQueue queue = new();
            queue.Load(CreateTracks("a1.mp3", "a2.mp3"));

            _ = queue.StartAt(-3);

            Assert.Same(queue.List.Items[0], queue.List.StartingItem);
        }

        private List<string> CreateTracks(params string[] fileNames)
        {
            List<string> paths = [];
            foreach (string fileName in fileNames)
            {
                string path = Path.Combine(_directory, fileName);
                File.WriteAllBytes(path, []);
                paths.Add(path);
            }

            return paths;
        }
    }
}
