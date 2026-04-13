using System.Diagnostics.CodeAnalysis;
using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für die statische Hilfsmethode <see cref="BackgroundProviderIdService.ExtractITunesCollectionId"/>.
    /// </summary>
    public sealed class BackgroundProviderIdServiceTests
    {
        [Theory]
        [InlineData("https://music.apple.com/de/album/die-drei-folge-1/id1234567890", "1234567890")]
        [InlineData("https://music.apple.com/de/album/some-album/1234567890", "1234567890")]
        [InlineData("https://music.apple.com/de/album/folge-87/id999?i=42", "999")]
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "xUnit-InlineData liefert Theorie-Parameter ausschließlich als primitive Werte; die getestete Hilfsmethode nimmt bewusst einen string entgegen.")]
        public void ExtractITunesCollectionId_ValidUrl_ReturnsId(string url, string expectedId)
        {
            string? result = BackgroundProviderIdService.ExtractITunesCollectionId(url);

            Assert.Equal(expectedId, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://example.com/no-id-here")]
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "xUnit-InlineData liefert Theorie-Parameter ausschließlich als primitive Werte; die getestete Hilfsmethode nimmt bewusst einen string entgegen.")]
        public void ExtractITunesCollectionId_InvalidUrl_ReturnsNull(string? url)
        {
            string? result = BackgroundProviderIdService.ExtractITunesCollectionId(url);

            Assert.Null(result);
        }
    }
}
