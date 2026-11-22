using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services;
using EchoPlay.TagManager.Tests.Fakes;

namespace EchoPlay.TagManager.Tests.Services
{
    /// <summary>
    /// Prüft, dass eine Umbenennung auf einen bereits belegten Namen unterbleibt.
    /// </summary>
    /// <remarks>
    /// Der neue Dateiname entsteht aus einem Muster und den Werten aus der Kennzeichnung.
    /// Trifft er einen Namen, den es schon gibt, gehört die Datei einer anderen Aufnahme.
    ///
    /// Die Tests arbeiten auf echten Dateien im Temp-Verzeichnis.
    /// </remarks>
    public sealed class FileRenameGuardTests
    {
        [Fact]
        public async Task Rename_WhenTheTargetNameIsTaken_SkipsThatFile()
        {
            string folder = CreateTempFolder();
            try
            {
                string source = Path.Combine(folder, "01 - Alt.mp3");
                string occupied = Path.Combine(folder, "Neuer Name.mp3");
                await File.WriteAllTextAsync(source, "ton", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(occupied, "anderer ton", TestContext.Current.CancellationToken);

                FileRenameService sut = new(new FakeLoggerFactory());

                List<(string FilePath, AudioTag Tag)> dateien =
                [
                    (source, new AudioTag { Title = "Neuer Name" }),
                ];

                int erfolge = await sut.RenameAsync(dateien, "{title}");

                // Überschreiben wäre Datenverlust: Die belegte Datei ist eine andere
                // Aufnahme, nicht dieselbe unter anderem Namen.
                Assert.Equal(0, erfolge);
                Assert.True(File.Exists(source));
                Assert.Equal(
                    "anderer ton",
                    await File.ReadAllTextAsync(occupied, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-rename-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}
