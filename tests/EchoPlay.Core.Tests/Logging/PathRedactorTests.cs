using System;
using EchoPlay.Core.Logging;

namespace EchoPlay.Core.Tests.Logging
{
    /// <summary>
    /// Verifiziert, dass <see cref="PathRedactor"/> Username- und tiefe Verzeichnisinformationen
    /// aus Pfaden entfernt und gleichzeitig stabile Korrelations-Hashes liefert.
    /// </summary>
    public sealed class PathRedactorTests
    {
        [Fact]
        public void Redact_FullUserPath_HidesDirectoryButKeepsFileName()
        {
            string input = @"C:\Users\Renes\Music\TKKG\Folge42.mp3";

            string result = PathRedactor.Redact(input);

            Assert.DoesNotContain("Renes", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Users", result, StringComparison.Ordinal);
            Assert.Contains("Folge42.mp3", result, StringComparison.Ordinal);
        }

        [Fact]
        public void Redact_FileNameOnly_ReturnsFileNameUntouched()
        {
            // Reiner Dateiname (ohne Verzeichnis) enthält per Definition keine User-PII.
            string result = PathRedactor.Redact("Folge42.mp3");

            Assert.Equal("Folge42.mp3", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Redact_NullOrEmpty_ReturnsPlaceholder(string? input)
        {
            string result = PathRedactor.Redact(input);

            Assert.Equal("(kein Pfad)", result);
        }

        [Fact]
        public void Redact_SamePath_ReturnsStableHash()
        {
            string input = @"C:\Users\Renes\Music\TKKG\Folge42.mp3";

            string first = PathRedactor.Redact(input);
            string second = PathRedactor.Redact(input);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Redact_DifferentDirectories_ProduceDifferentHashes()
        {
            string a = @"C:\Users\Alice\Music\song.mp3";
            string b = @"C:\Users\Bob\Music\song.mp3";

            string redactedA = PathRedactor.Redact(a);
            string redactedB = PathRedactor.Redact(b);

            Assert.NotEqual(redactedA, redactedB);
        }

        [Fact]
        public void Redact_CaseInsensitive_SameDirectoryProducesSameHash()
        {
            // Windows-Dateisystem ist Case-Insensitive; gleiche Verzeichnisse müssen denselben
            // Hash liefern, damit Log-Korrelation auch bei Schreibweisen-Drift funktioniert.
            string upper = @"C:\USERS\Renes\Music\song.mp3";
            string lower = @"C:\users\Renes\Music\song.mp3";

            string redactedUpper = PathRedactor.Redact(upper);
            string redactedLower = PathRedactor.Redact(lower);

            Assert.Equal(redactedUpper, redactedLower);
        }
    }
}
