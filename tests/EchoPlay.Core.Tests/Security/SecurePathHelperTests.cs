using System.IO;
using EchoPlay.Core.Security;

namespace EchoPlay.Core.Tests.Security
{
    /// <summary>
    /// Verifiziert die Pfad-Sicherheitsprüfung gegen Traversal-Angriffe und Symlink-Escape.
    /// </summary>
    public sealed class SecurePathHelperTests
    {
        [Fact]
        public void IsPathInside_PathDirectlyInsideRoot_ReturnsTrue()
        {
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");
            string candidate = Path.Combine(root, "cover.jpg");

            Assert.True(SecurePathHelper.IsPathInside(candidate, root));
        }

        [Fact]
        public void IsPathInside_PathInSubdirectoryOfRoot_ReturnsTrue()
        {
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");
            string candidate = Path.Combine(root, "TKKG", "Folge42", "cover.jpg");

            Assert.True(SecurePathHelper.IsPathInside(candidate, root));
        }

        [Fact]
        public void IsPathInside_PathOutsideRoot_ReturnsFalse()
        {
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");
            string candidate = Path.Combine(Path.GetTempPath(), "anderer-ordner", "cover.jpg");

            Assert.False(SecurePathHelper.IsPathInside(candidate, root));
        }

        [Fact]
        public void IsPathInside_RelativeDotDotEscape_ReturnsFalse()
        {
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");
            // Aufloesung von ..-Anteilen muss erkennen, dass das Ergebnis ausserhalb des Roots liegt.
            string candidate = Path.Combine(root, "..", "geheim", "cover.jpg");

            Assert.False(SecurePathHelper.IsPathInside(candidate, root));
        }

        [Fact]
        public void IsPathInside_PrefixCollisionWithSiblingDirectory_ReturnsFalse()
        {
            // "/foo" darf nicht als Prefix von "/foo-evil" durchgehen.
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");
            string candidate = Path.Combine(Path.GetTempPath(), "echoplay-root-evil", "cover.jpg");

            Assert.False(SecurePathHelper.IsPathInside(candidate, root));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(null, "C:\\foo")]
        [InlineData("C:\\foo", null)]
        [InlineData("", "C:\\foo")]
        [InlineData("C:\\foo", "")]
        [InlineData("   ", "C:\\foo")]
        public void IsPathInside_NullOrEmptyArgument_ReturnsFalse(string? candidate, string? root)
        {
            Assert.False(SecurePathHelper.IsPathInside(candidate, root));
        }

        [Fact]
        public void IsPathInside_RootEqualsCandidate_ReturnsTrue()
        {
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");

            // Wurzel selbst gilt als "innen" — sonst koennten Cleanup-Routinen ihren eigenen Root nicht prüfen.
            Assert.True(SecurePathHelper.IsPathInside(root, root));
        }

        [Fact]
        public void IsPathInside_TrailingSeparatorMix_ReturnsTrue()
        {
            string root = Path.Combine(Path.GetTempPath(), "echoplay-root");
            string rootWithSep = root + Path.DirectorySeparatorChar;
            string candidate = Path.Combine(root, "cover.jpg");

            // Verschiedene Trailing-Separator-Kombinationen muessen gleich behandelt werden.
            Assert.True(SecurePathHelper.IsPathInside(candidate, rootWithSep));
        }
    }
}
