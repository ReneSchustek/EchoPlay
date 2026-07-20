using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.Data.Tests.Entities
{
    /// <summary>
    /// Tests für <see cref="ProviderTypeExtensions.Includes"/>.
    /// Prüft die korrekte Auflösung von <see cref="ProviderType.Both"/>
    /// sowie die Einzelwerte.
    /// </summary>
    public sealed class ProviderTypeExtensionsTests
    {
        [Fact]
        public void Includes_Spotify_ForBoth_ReturnsTrue()
        {
            bool result = ProviderType.Both.Includes(ProviderType.Spotify);

            Assert.True(result);
        }

        [Fact]
        public void Includes_AppleMusic_ForBoth_ReturnsTrue()
        {
            bool result = ProviderType.Both.Includes(ProviderType.AppleMusic);

            Assert.True(result);
        }

        [Fact]
        public void Includes_None_ForBoth_ReturnsFalse()
        {
            bool result = ProviderType.Both.Includes(ProviderType.None);

            Assert.False(result);
        }

        [Fact]
        public void Includes_Spotify_ForSpotify_ReturnsTrue()
        {
            bool result = ProviderType.Spotify.Includes(ProviderType.Spotify);

            Assert.True(result);
        }

        [Fact]
        public void Includes_AppleMusic_ForSpotify_ReturnsFalse()
        {
            bool result = ProviderType.Spotify.Includes(ProviderType.AppleMusic);

            Assert.False(result);
        }
    }
}
