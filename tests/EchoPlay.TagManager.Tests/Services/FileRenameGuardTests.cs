using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services;
using EchoPlay.TagManager.Tests.Fakes;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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
            string ordner = CreateTempFolder();
            try
            {
                string quelle = Path.Combine(ordner, "01 - Alt.mp3");
                string belegt = Path.Combine(ordner, "Neuer Name.mp3");
                await File.WriteAllTextAsync(quelle, "ton", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(belegt, "anderer ton", TestContext.Current.CancellationToken);

                FileRenameService sut = new(new FakeLoggerFactory());

                List<(string FilePath, AudioTag Tag)> dateien =
                [
                    (quelle, new AudioTag { Title = "Neuer Name" }),
                ];

                int erfolge = await sut.RenameAsync(dateien, "{title}");

                // Überschreiben wäre Datenverlust: Die belegte Datei ist eine andere
                // Aufnahme, nicht dieselbe unter anderem Namen.
                Assert.Equal(0, erfolge);
                Assert.True(File.Exists(quelle));
                Assert.Equal(
                    "anderer ton",
                    await File.ReadAllTextAsync(belegt, TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(ordner, recursive: true);
            }
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static string CreateTempFolder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"echoplay-rename-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}
