using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace EchoPlay.App.Tests
{
    /// <summary>
    /// Prüft, dass das Splash-Logo als Embedded-Resource in <c>EchoPlay.App</c> verfügbar ist.
    /// Hintergrund: Die <c>SplashWindow</c>-Implementierung lädt das Logo per
    /// <see cref="Assembly.GetManifestResourceStream(string)"/>; fehlt der Eintrag, greift
    /// in Produktion der Fallback nicht — der Splash bliebe ohne Logo.
    /// </summary>
    public sealed class SplashWindowEmbeddedLogoTests
    {
        private const string LogoResourceName = "EchoPlay.App.Assets.logo.png";

        [Fact]
        public void EchoPlayAppAssembly_ContainsSplashLogoEmbeddedResource()
        {
            Assembly appAssembly = typeof(SplashWindow).Assembly;

            string[] resourceNames = appAssembly.GetManifestResourceNames();

            Assert.Contains(LogoResourceName, resourceNames);
        }

        [Fact]
        public void EchoPlayAppAssembly_LogoStream_IsReadableAndNonEmpty()
        {
            Assembly appAssembly = typeof(SplashWindow).Assembly;

            using Stream? stream = appAssembly.GetManifestResourceStream(LogoResourceName);

            Assert.NotNull(stream);
            Assert.True(stream!.Length > 0, "Embedded-Logo darf nicht leer sein.");
        }

        [Fact]
        public void EchoPlayAppAssembly_LogoStream_StartsWithPngSignature()
        {
            Assembly appAssembly = typeof(SplashWindow).Assembly;

            using Stream? stream = appAssembly.GetManifestResourceStream(LogoResourceName);
            Assert.NotNull(stream);

            byte[] header = new byte[8];
            int read = stream!.Read(header, 0, header.Length);

            Assert.Equal(8, read);
            // PNG-Signatur: 89 50 4E 47 0D 0A 1A 0A — fängt versehentliche Vertauschung
            // mit JPEG/ICO/anderen Assets ab, falls jemand die csproj-EmbeddedResource ändert.
            byte[] expected = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            Assert.Equal(expected, header);
        }

        [Fact]
        public void EchoPlayAppAssembly_NoStaleAssetsLogoEntry_WithDifferentNamespace()
        {
            Assembly appAssembly = typeof(SplashWindow).Assembly;

            string[] logoResources = appAssembly.GetManifestResourceNames()
                .Where(name => name.EndsWith("logo.png", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Nur ein Eintrag erwartet — verhindert dass ein doppelter Include unter anderem
            // Namespace eingeschmuggelt wird (z. B. wenn jemand Default-Heuristiken aktiviert).
            _ = Assert.Single(logoResources);
            Assert.Equal(LogoResourceName, logoResources[0]);
        }
    }
}
