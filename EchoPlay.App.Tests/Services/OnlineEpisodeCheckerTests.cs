using EchoPlay.App.Services;
using EchoPlay.App.Tests.Helpers;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für die statischen Hilfsmethoden des <see cref="OnlineEpisodeChecker"/>.
    /// Prüft die Nummernextraktion aus iTunes-Albumnamen und die lokale Ordneranalyse.
    /// </summary>
    public sealed class OnlineEpisodeCheckerTests
    {
        // ── ExtractEpisodeNumber ────────────────────────────────────────────────

        [Fact]
        public void ExtractEpisodeNumber_StandardFormat_ReturnsNumber()
        {
            // "Die drei ??? - 229 - Drehbuch der Täuschung" → 229
            int? result = OnlineEpisodeChecker.ExtractEpisodeNumber(
                "Die drei ??? - 229 - Drehbuch der Täuschung");

            Assert.Equal(229, result);
        }

        [Fact]
        public void ExtractEpisodeNumber_FolgePrefix_ReturnsNumber()
        {
            // "TKKG - Folge 42 - Titel" → häufiges Muster bei iTunes-Hörspielen
            int? result = OnlineEpisodeChecker.ExtractEpisodeNumber(
                "TKKG - Folge 42 - Titel");

            Assert.Equal(42, result);
        }

        [Fact]
        public void ExtractEpisodeNumber_NumberOnly_ReturnsNumber()
        {
            // Manche Alben haben nur die Nummer im Titel
            int? result = OnlineEpisodeChecker.ExtractEpisodeNumber("001 - Der Super-Papagei");

            Assert.Equal(1, result);
        }

        [Fact]
        public void ExtractEpisodeNumber_NoNumber_ReturnsNull()
        {
            // Sonderalben wie "Best of" haben keine Episodennummer
            int? result = OnlineEpisodeChecker.ExtractEpisodeNumber("Best of Die drei ???");

            // "Best" enthält keine Zahl, aber "drei" auch nicht –
            // die Regex findet jedoch die 3 in "drei" nicht (das sind Buchstaben).
            // Tatsächlich: "???" enthält keine Zahl → null
            Assert.Null(result);
        }

        [Fact]
        public void ExtractEpisodeNumber_EmptyString_ReturnsNull()
        {
            int? result = OnlineEpisodeChecker.ExtractEpisodeNumber(string.Empty);

            Assert.Null(result);
        }

        [Fact]
        public void ExtractEpisodeNumber_ThreeDigitNumber_ReturnsCorrectly()
        {
            // Dreistellige Nummer – typisch für langlebige Serien
            int? result = OnlineEpisodeChecker.ExtractEpisodeNumber(
                "Die drei ??? - 100 - Toteninsel");

            Assert.Equal(100, result);
        }

        // ── GetLocalHighestEpisodeNumber ───────────────────────────────────────

        [Fact]
        public void GetLocalHighestEpisodeNumber_NullPath_ReturnsZero()
        {
            // Serien ohne lokalen Ordner liefern 0
            int result = OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(null);

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetLocalHighestEpisodeNumber_NonExistentPath_ReturnsZero()
        {
            // Nicht existierender Pfad darf keinen Fehler werfen
            int result = OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(
                @"C:\NichtExistierenderPfad\12345");

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetLocalHighestEpisodeNumber_EmptyDirectory_ReturnsZero()
        {
            // Leerer Ordner hat keine Episoden
            string tempDir = Path.Combine(Path.GetTempPath(), $"echoplay_test_{TestIds.Indexed(1):N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                int result = OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(tempDir);
                Assert.Equal(0, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetLocalHighestEpisodeNumber_NumberedFolders_ReturnsHighest()
        {
            // Simuliert eine Serienstruktur mit nummerierten Episodenordnern
            string tempDir = Path.Combine(Path.GetTempPath(), $"echoplay_test_{TestIds.Indexed(2):N}");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "001 - Erste Folge"));
            Directory.CreateDirectory(Path.Combine(tempDir, "002 - Zweite Folge"));
            Directory.CreateDirectory(Path.Combine(tempDir, "010 - Zehnte Folge"));

            try
            {
                int result = OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(tempDir);
                Assert.Equal(10, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetLocalHighestEpisodeNumber_WithSpecialFolders_IgnoresThem()
        {
            // Sonderfolgen (Nummer 0) und Ordner ohne Nummer werden ignoriert
            string tempDir = Path.Combine(Path.GetTempPath(), $"echoplay_test_{TestIds.Indexed(3):N}");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "000 - Sonderfolge"));
            Directory.CreateDirectory(Path.Combine(tempDir, "005 - Fünfte Folge"));
            Directory.CreateDirectory(Path.Combine(tempDir, "Bonus - Extra"));

            try
            {
                int result = OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(tempDir);
                Assert.Equal(5, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
