using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Die Wege, die der Prüfablauf nimmt, wenn etwas schiefgeht: abgebrochener Abruf,
    /// eine Serie ohne ermittelbare Künstler-Kennung, ein Künstler ohne Alben und ein
    /// Ordner, der da ist, sich aber nicht auflisten lässt.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zwischen Abbruch und Fehler ist der Kern: Ein Fehler bei einer
    /// einzelnen Serie darf den Durchlauf nicht beenden, ein Abbruch dagegen schon —
    /// sonst läuft die Prüfung nach dem Schließen des Fensters weiter.
    /// </remarks>
    public sealed class OnlineEpisodeCheckerErrorPathsTests
    {
        private const long ArtistId = 4711;

        private static CheckableSeriesInfo Checkable(string? artistId, string? localFolder = null) => new()
        {
            SeriesId = TestIds.SeriesA,
            Title = "Testserie",
            AppleMusicArtistId = artistId,
            LocalFolderPath = localFolder
        };

        private static OnlineEpisodeChecker BuildChecker(FakeAppleMusicSearchClient client) =>
            new(client, new FakeSeriesDataService(), new FakeLoggerFactory(), new FakeClock());

        /// <summary>
        /// Ein Abbruch während des Album-Abrufs beendet den ganzen Durchlauf. Er darf nicht
        /// wie ein gewöhnlicher Fehler behandelt und stillschweigend übersprungen werden.
        /// </summary>
        [Fact]
        public async Task CheckAllAsync_WhenTheLookupIsCancelled_StopsInsteadOfSkipping()
        {
            FakeAppleMusicSearchClient client = new(
                lookupFailure: () => new OperationCanceledException());

            OnlineEpisodeChecker checker = BuildChecker(client);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => checker.CheckAllAsync(
                    [Checkable(ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    CancellationToken.None));
        }

        /// <summary>
        /// Ein gewöhnlicher Fehler beim Abruf bleibt dagegen bei der einen Serie: Der Durchlauf
        /// endet ohne Ergebnis, aber ohne Ausnahme.
        /// </summary>
        [Fact]
        public async Task CheckAllAsync_WhenTheLookupFails_KeepsGoingWithoutResult()
        {
            FakeAppleMusicSearchClient client = new(
                lookupFailure: () => new InvalidOperationException("iTunes antwortet nicht"));

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
        }

        /// <summary>
        /// Ohne hinterlegte Kennung und ohne Treffer in der Künstlersuche gibt es nichts zu
        /// prüfen. Die Serie wird übersprungen, die Albenabfrage unterbleibt.
        /// </summary>
        [Fact]
        public async Task CheckAllAsync_WithoutAnyResolvableArtist_SkipsTheSeries()
        {
            FakeAppleMusicSearchClient client = new();

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(artistId: null)],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
            Assert.Equal(1, client.SearchArtistsCallCount);
            Assert.Equal(0, client.LookupAlbumsCallCount);
        }

        /// <summary>
        /// Ein Künstler, der gefunden wird, aber kein einziges Album führt, liefert kein
        /// Ergebnis — und keinen leeren Eintrag, der in der Oberfläche als Serie ohne
        /// Neuerscheinungen erschiene.
        /// </summary>
        [Fact]
        public async Task CheckAllAsync_WhenTheArtistHasNoAlbums_YieldsNoResult()
        {
            FakeAppleMusicSearchClient client = new(
                albumsByArtist: new Dictionary<long, List<ITunesCollectionDto>> { [ArtistId] = [] });

            OnlineEpisodeChecker checker = BuildChecker(client);

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync(
                [Checkable(ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                TestContext.Current.CancellationToken);

            Assert.Empty(results);
            Assert.Equal(1, client.LookupAlbumsCallCount);
        }

        /// <summary>
        /// Der Ordner ist da, lässt sich aber nicht auflisten — der Alltagsfall eines
        /// Netzlaufwerks ohne Verbindung. Erwartet wird 0 und keine Ausnahme: Die
        /// Onlineprüfung läuft dann ohne lokalen Abgleich weiter.
        /// </summary>
        [Fact]
        public void GetLocalHighestEpisodeNumber_WithAFolderThatCannotBeListed_ReturnsZero()
        {
            string folder = CreateUnreadableFolder();
            try
            {
                Assert.Equal(0, OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(folder));
            }
            finally
            {
                RestoreAndDelete(folder);
            }
        }

        /// <summary>
        /// Legt einen eigenen Ordner im Temp-Verzeichnis an und entzieht sich selbst das
        /// Leserecht. Nur so entsteht der Zustand „da, aber nicht lesbar" — ein fehlender
        /// Ordner wird vorher abgefangen und erreicht den Zweig gar nicht.
        /// </summary>
        private static string CreateUnreadableFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-gesperrt-{Path.GetRandomFileName()}");
            DirectoryInfo folder = Directory.CreateDirectory(path);

            DirectorySecurity security = folder.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny));
            folder.SetAccessControl(security);

            return path;
        }

        /// <summary>
        /// Nimmt die Sperre zurück und räumt den Ordner weg. Läuft auch dann, wenn der Test
        /// scheitert — ein gesperrter Ordner darf nicht liegen bleiben.
        /// </summary>
        private static void RestoreAndDelete(string path)
        {
            DirectoryInfo folder = new(path);
            if (!folder.Exists)
            {
                return;
            }

            DirectorySecurity security = folder.GetAccessControl();
            security.RemoveAccessRuleAll(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ListDirectory | FileSystemRights.ReadData,
                AccessControlType.Deny));
            folder.SetAccessControl(security);

            folder.Delete(recursive: true);
        }
    }
}
