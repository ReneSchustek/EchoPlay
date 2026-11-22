using EchoPlay.Core.Replication;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="BusinessKey"/>.
    /// </summary>
    public sealed class BusinessKeyTests
    {
        [Fact]
        public void Series_LocalAndOnlineWithSameTitle_GetDifferentKeys()
        {
            // Lokal und online sind gewollt zwei Serien; im Abgleich dürfen sie nicht zusammenfallen.
            string local = BusinessKey.ForSeries(isOnlineImported: false, null, null, "TKKG");
            string online = BusinessKey.ForSeries(isOnlineImported: true, null, null, "TKKG");

            Assert.NotEqual(local, online);
        }

        [Fact]
        public void Series_Local_IgnoresCachedProviderId()
        {
            // Der Folgencheck trägt bei lokalen Serien eine Apple-Kennung nach, aber nicht auf jedem Gerät.
            Assert.Equal(
                BusinessKey.ForSeries(false, null, null, "TKKG"),
                BusinessKey.ForSeries(false, "12345", null, "TKKG"));
        }

        [Fact]
        public void Series_Online_PrefersAppleThenSpotify()
        {
            Assert.Equal("online:apple:123", BusinessKey.ForSeries(true, " 123 ", "abc", "TKKG"));
            Assert.Equal("online:spotify:abc", BusinessKey.ForSeries(true, null, "abc", "TKKG"));
        }

        [Fact]
        public void Series_TitleVariants_GetSameKey()
        {
            Assert.Equal(
                BusinessKey.ForSeries(false, null, null, "Die drei ???"),
                BusinessKey.ForSeries(false, null, null, "DIE DREI ???"));
        }

        [Fact]
        public void Episode_Local_UsesNumberWithinSeries()
        {
            string series = BusinessKey.ForSeries(false, null, null, "TKKG");

            Assert.Equal(series + "#42", BusinessKey.ForEpisode(series, false, "999", null, 42, "Folge 42"));
        }

        [Fact]
        public void Episode_Online_PrefersAlbumOverNumber()
        {
            string series = BusinessKey.ForSeries(true, "123", null, "TKKG");

            Assert.Equal(series + "|apple:999", BusinessKey.ForEpisode(series, true, "999", null, 42, "Folge 42"));
        }

        [Fact]
        public void Episode_WithoutNumberOrAlbum_UsesTitle()
        {
            string series = BusinessKey.ForSeries(false, null, null, "TKKG");

            Assert.Equal(
                BusinessKey.ForEpisode(series, false, null, null, null, "Das Sonderheft"),
                BusinessKey.ForEpisode(series, false, null, null, null, "das sonderheft"));
        }
    }
}
