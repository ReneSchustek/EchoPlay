using EchoPlay.Data.Infrastructure;

namespace EchoPlay.Data.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="FileDeviceIdentity"/> in einem eigenen Temp-Ordner.
    /// </summary>
    public sealed class FileDeviceIdentityTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"echoplay_device_{Guid.NewGuid():N}");

        public FileDeviceIdentityTests() => _ = Directory.CreateDirectory(_directory);

        [Fact]
        public void FirstAccess_CreatesTheFile_AndLaterInstancesReadTheSameId()
        {
            Guid first = new FileDeviceIdentity(_directory).DeviceId;
            Guid second = new FileDeviceIdentity(_directory).DeviceId;

            Assert.NotEqual(Guid.Empty, first);
            Assert.Equal(first, second);
            Assert.True(File.Exists(Path.Combine(_directory, FileDeviceIdentity.FileName)));
        }

        [Theory]
        [InlineData("keine Kennung")]
        [InlineData("00000000000000000000000000000000")]
        [InlineData("")]
        public void InvalidContent_IsReplacedByANewId(string content)
        {
            string path = Path.Combine(_directory, FileDeviceIdentity.FileName);
            File.WriteAllText(path, content);

            Guid id = new FileDeviceIdentity(_directory).DeviceId;

            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal(id.ToString("N"), File.ReadAllText(path));
        }

        [Fact]
        public void MissingDirectory_IsCreated()
        {
            string nested = Path.Combine(_directory, "neu");

            Guid id = new FileDeviceIdentity(nested).DeviceId;

            Assert.NotEqual(Guid.Empty, id);
            Assert.True(Directory.Exists(nested));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
