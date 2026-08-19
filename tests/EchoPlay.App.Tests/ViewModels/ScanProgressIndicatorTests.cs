using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.UI.Xaml;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Fortschrittsanzeige der unteren Leiste: wann sie erscheint, wann der
    /// Balken ohne festen Wert läuft und wann sie wieder verschwindet.
    /// </summary>
    /// <remarks>
    /// Das Zeichen der Taskleiste hängt am selben Zustand. Es lässt sich im Testlauf nicht
    /// beobachten — ohne Fenster greift es ins Leere —, der sichtbare Teil der Zusage
    /// dagegen schon.
    /// </remarks>
    public sealed class ScanProgressIndicatorTests
    {
        [Fact]
        public void NewIndicator_ShowsNothing()
        {
            ScanProgressIndicator sut = new(new TaskbarProgressService());

            Assert.False(sut.IsActive);
            Assert.Equal(Visibility.Collapsed, sut.Visibility);
            Assert.Equal(string.Empty, sut.Text);
        }

        [Fact]
        public void Update_WithPercentage_ShowsTextAndValue()
        {
            ScanProgressIndicator sut = new(new TaskbarProgressService());

            sut.Update(new ScanProgress { StatusText = "Lese TKKG …", ProcessedFiles = 42, TotalFiles = 100 });

            Assert.True(sut.IsActive);
            Assert.Equal(Visibility.Visible, sut.Visibility);
            Assert.Equal("Lese TKKG …", sut.Text);
            Assert.Equal(42, sut.Value);
            Assert.False(sut.IsIndeterminate);
        }

        [Fact]
        public void Update_WithoutPercentage_LetsTheBarRunFreely()
        {
            ScanProgressIndicator sut = new(new TaskbarProgressService());

            sut.Update(new ScanProgress { StatusText = "Zähle Ordner …" });

            // Solange der Umfang unbekannt ist, wäre ein Balken bei null irreführend —
            // er sähe aus, als ginge nichts voran.
            Assert.True(sut.IsIndeterminate);
        }

        [Fact]
        public void SetText_ShowsTheTextWithoutAValue()
        {
            ScanProgressIndicator sut = new(new TaskbarProgressService());

            sut.SetText("Bereite vor …");

            Assert.Equal("Bereite vor …", sut.Text);
            Assert.Equal(0, sut.Value);
            Assert.True(sut.IsActive);
            Assert.True(sut.IsIndeterminate);
        }

        [Fact]
        public void Clear_HidesTheIndicatorAndForgetsTheText()
        {
            ScanProgressIndicator sut = new(new TaskbarProgressService());
            sut.Update(new ScanProgress { StatusText = "Lese TKKG …", ProcessedFiles = 42, TotalFiles = 100 });

            sut.Clear();

            Assert.False(sut.IsActive);
            Assert.Equal(Visibility.Collapsed, sut.Visibility);
            Assert.Equal(string.Empty, sut.Text);
            Assert.Equal(0, sut.Value);
        }

        [Fact]
        public void Update_WithoutProgress_ThrowsArgumentNullException()
        {
            ScanProgressIndicator sut = new(new TaskbarProgressService());

            _ = Assert.Throws<System.ArgumentNullException>(() => sut.Update(null!));
        }
    }
}
