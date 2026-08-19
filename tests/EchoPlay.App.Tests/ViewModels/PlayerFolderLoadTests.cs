using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.IO;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Öffnen eines Ordners im Abspieler: Welche Dateien in die Wiedergabeliste
    /// kommen und in welcher Reihenfolge.
    /// </summary>
    /// <remarks>
    /// Der Ordner ist die einfachste Art, ein Hörspiel abzuspielen, das nicht in der
    /// Mediathek steht. Käme dabei die Reihenfolge durcheinander, hörte der Anwender die
    /// Folge in Bruchstücken — ohne dass etwas nach einem Fehler aussieht.
    ///
    /// Die Tests legen echte Dateien im Temp-Verzeichnis an und räumen sie wieder ab; der
    /// geprüfte Weg liest ein Verzeichnis und ist anders nicht zu erreichen.
    /// </remarks>
    public sealed class PlayerFolderLoadTests
    {
        [Fact]
        public void LoadFolder_TakesOnlyAudioFiles()
        {
            string folder = CreateFolder("02 - Zweite.mp3", "01 - Erste.mp3", "hinweis.txt");
            try
            {
                PlayerViewModel sut = Build();

                sut.LoadFolder(folder);

                Assert.Equal(2, sut.PlaylistItems.Count);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void LoadFolder_SortsByFileName()
        {
            string folder = CreateFolder("03 - Dritte.mp3", "01 - Erste.mp3", "02 - Zweite.mp3");
            try
            {
                PlayerViewModel sut = Build();

                sut.LoadFolder(folder);

                // Die Dateinamen tragen die Reihenfolge; das Dateisystem liefert sie nicht
                // zwingend sortiert.
                Assert.Equal(
                    [@"01 - Erste.mp3", @"02 - Zweite.mp3", @"03 - Dritte.mp3"],
                    [.. FileNames(sut)]);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void LoadFolder_WithoutAudioFiles_LeavesThePlaylistEmpty()
        {
            string folder = CreateFolder("liesmich.txt");
            try
            {
                PlayerViewModel sut = Build();

                sut.LoadFolder(folder);

                Assert.Empty(sut.PlaylistItems);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void NewViewModel_ShowsTheCoverPlaceholder()
        {
            PlayerViewModel sut = Build();

            Assert.Null(sut.CoverImage);
            Assert.Equal(Visibility.Visible, sut.NoCoverVisibility);
            Assert.NotNull(sut.Volume);
            Assert.NotNull(sut.Time);
        }

        private static System.Collections.Generic.IEnumerable<string> FileNames(PlayerViewModel sut)
        {
            foreach (PlaylistItemViewModel item in sut.PlaylistItems)
            {
                yield return Path.GetFileName(item.FullPath);
            }
        }

        private static PlayerViewModel Build()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());
            _ = services.AddScoped<ILocalCoverLoader>(_ => new FakeLocalCoverLoader());
            ServiceProvider provider = services.BuildServiceProvider();

            return new PlayerViewModel(
                new FakePlayerService(), provider.GetRequiredService<IServiceScopeFactory>());
        }

        private static string CreateFolder(params string[] fileNames)
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-player-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);

            foreach (string name in fileNames)
            {
                File.WriteAllBytes(Path.Combine(path, name), []);
            }

            return path;
        }
    }
}
