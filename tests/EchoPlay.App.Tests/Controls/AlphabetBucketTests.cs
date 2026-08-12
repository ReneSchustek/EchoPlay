using EchoPlay.App.Controls;
using Xunit;

namespace EchoPlay.App.Tests.Controls
{
    /// <summary>
    /// Sichert ab, dass belegter und aktiver Zustand eines Fachs nicht allein an der Farbe
    /// hängen — sie sind über Schriftschnitt und Rahmen erkennbar.
    /// </summary>
    public sealed class AlphabetBucketTests
    {
        [Fact]
        public void CurrentBucket_IsBoldAndFramed()
        {
            AlphabetBucket bucket = new('M', isOccupied: true, isCurrent: true, "Zu M springen");

            Assert.Equal(AlphabetBucket.BoldWeight, bucket.LabelWeight.Weight);
            Assert.True(bucket.BorderStrength.Left > 0, "Der aktive Buchstabe braucht einen sichtbaren Rahmen.");
        }

        [Fact]
        public void OrdinaryBucket_IsNeitherBoldNorFramed()
        {
            AlphabetBucket bucket = new('M', isOccupied: true, isCurrent: false, "Zu M springen");

            Assert.Equal(AlphabetBucket.NormalWeight, bucket.LabelWeight.Weight);
            Assert.Equal(0, bucket.BorderStrength.Left);
        }

        [Fact]
        public void EmptyBucket_IsDisabledButStillPresent()
        {
            AlphabetBucket bucket = new('Q', isOccupied: false, isCurrent: false, "Zu Q springen");

            Assert.False(bucket.IsOccupied);
            Assert.Equal("Q", bucket.Label);
        }

        [Fact]
        public void Label_ShowsTheLetterItself() =>
            Assert.Equal("#", new AlphabetBucket('#', false, false, "Übrige").Label);
    }
}
