using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="TagManagerViewModel"/>.
    /// Prüft die Auto-Lookup-Logik: Anfragebau aus Ordnerkontext und Trefferauswahl.
    /// </summary>
    public sealed class TagManagerViewModelTests
    {
        // ── BuildAutoLookupQuery ──────────────────────────────────────────────────

        [Fact]
        public void BuildAutoLookupQuery_ReturnsSeriesAndEpisode_FromFolderPath()
        {
            // Serienname aus übergeordnetem Ordner, Folgentitel aus Ordnername (ohne Laufnummer)
            string path = @"D:\Hörspiele\Die drei Fragezeichen\001 - Der Super-Papagei";

            string query = TagManagerViewModel.BuildAutoLookupQuery(path);

            Assert.Equal("Die drei Fragezeichen Der Super-Papagei", query);
        }

        [Fact]
        public void BuildAutoLookupQuery_StripsLeadingNumber_FromEpisodeFolderName()
        {
            // "042 - Der Fluch des Drachen" → "Der Fluch des Drachen" (Laufnummer entfernt)
            string path = @"D:\Hörspiele\TKKG\042 - Der Fluch des Drachen";

            string query = TagManagerViewModel.BuildAutoLookupQuery(path);

            Assert.Equal("TKKG Der Fluch des Drachen", query);
        }

        [Fact]
        public void BuildAutoLookupQuery_ReturnsEmpty_WhenFolderPathIsNull()
        {
            // Kein geöffneter Ordner → leere Suchanfrage, kein Absturz
            string query = TagManagerViewModel.BuildAutoLookupQuery(null);

            Assert.Equal(string.Empty, query);
        }

        [Fact]
        public void BuildAutoLookupQuery_ReturnsEmpty_WhenFolderPathIsEmpty()
        {
            string query = TagManagerViewModel.BuildAutoLookupQuery(string.Empty);

            Assert.Equal(string.Empty, query);
        }

        // ── SelectBestMatch ───────────────────────────────────────────────────────

        [Fact]
        public void SelectBestMatch_ReturnsExactTrackCountMatch_WhenAvailable()
        {
            // Ein Ergebnis hat exakt 8 Tracks – das ist der beste Treffer
            IReadOnlyList<TagLookupResult> results =
            [
                new TagLookupResult { Title = "Falsch", TrackCount = 5 },
                new TagLookupResult { Title = "Richtig", TrackCount = 8 },
                new TagLookupResult { Title = "Auch falsch", TrackCount = 12 }
            ];

            TagLookupResult? match = TagManagerViewModel.SelectBestMatch(results, loadedTrackCount: 8);

            Assert.NotNull(match);
            Assert.Equal("Richtig", match!.Title);
        }

        [Fact]
        public void SelectBestMatch_ReturnsFirstResult_WhenNoTrackCountMatches()
        {
            // Kein Ergebnis passt zur Track-Anzahl → erstes Ergebnis (MusicBrainz-Relevanzsortierung)
            IReadOnlyList<TagLookupResult> results =
            [
                new TagLookupResult { Title = "Erster Treffer", TrackCount = 3 },
                new TagLookupResult { Title = "Zweiter Treffer", TrackCount = 5 }
            ];

            TagLookupResult? match = TagManagerViewModel.SelectBestMatch(results, loadedTrackCount: 8);

            Assert.NotNull(match);
            Assert.Equal("Erster Treffer", match!.Title);
        }

        [Fact]
        public void SelectBestMatch_ReturnsFirstResult_WhenTrackCountIsNull()
        {
            // MusicBrainz liefert manchmal keine Track-Anzahl → erstes Ergebnis
            IReadOnlyList<TagLookupResult> results =
            [
                new TagLookupResult { Title = "Ohne TrackCount", TrackCount = null },
                new TagLookupResult { Title = "Zweiter",         TrackCount = null }
            ];

            TagLookupResult? match = TagManagerViewModel.SelectBestMatch(results, loadedTrackCount: 8);

            Assert.NotNull(match);
            Assert.Equal("Ohne TrackCount", match!.Title);
        }

        [Fact]
        public void SelectBestMatch_ReturnsNull_WhenResultsAreEmpty()
        {
            // Keine Ergebnisse → kein Treffer, kein Absturz
            TagLookupResult? match = TagManagerViewModel.SelectBestMatch([], loadedTrackCount: 8);

            Assert.Null(match);
        }

        // ── AutoLookupCommand – Integrations-Szenario ────────────────────────────

        [Fact]
        public async Task AutoLookupAsync_FiresAutoLookupApplied_WhenExactTrackCountMatches()
        {
            // Ein Ordner mit 3 Tracks – das MusicBrainz-Ergebnis hat exakt 3 Tracks → Auto-Apply
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag()),
                (@"D:\test\track2.mp3", new AudioTag()),
                (@"D:\test\track3.mp3", new AudioTag())
            ];

            FakeTagService tagService = new(folderFiles);
            FakeTagLookupService lookupService = new(
            [
                new TagLookupResult { Title = "Der Super-Papagei", Artist = "Die drei ???", TrackCount = 3 }
            ]);

            TagManagerViewModel vm = BuildViewModel(tagService, lookupService);

            // Ordner laden damit _files.Count = 3 und _currentFolderPath gesetzt ist
            await vm.LoadFolderAsync(@"D:\Hörspiele\Die drei Fragezeichen\001 - Der Super-Papagei");

            EchoPlay.App.Models.TagLookupCandidate? appliedResult = null;
            vm.AutoLookupApplied += (_, r) => appliedResult = r;

            vm.AutoLookupCommand.Execute(null);

            // Kurz warten: AutoLookupAsync ist async void über Command
            await Task.Delay(200);

            Assert.NotNull(appliedResult);
            Assert.Equal("Der Super-Papagei", appliedResult!.Title);
        }

        [Fact]
        public async Task AutoLookupAsync_FiresLookupResultsReady_WhenNoExactMatch()
        {
            // Track-Anzahl stimmt nicht überein → kein Auto-Apply, Auswahl-Dialog
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag()),
                (@"D:\test\track2.mp3", new AudioTag())
            ];

            FakeTagService tagService = new(folderFiles);
            FakeTagLookupService lookupService = new(
            [
                new TagLookupResult { Title = "Anderes Album", TrackCount = 5 }
            ]);

            TagManagerViewModel vm = BuildViewModel(tagService, lookupService);
            await vm.LoadFolderAsync(@"D:\Hörspiele\TKKG\001 - Die Jagd");

            IReadOnlyList<EchoPlay.App.Models.TagLookupCandidate>? readyResults = null;
            vm.LookupResultsReady += (_, r) => readyResults = r;

            vm.AutoLookupCommand.Execute(null);
            await Task.Delay(200);

            Assert.NotNull(readyResults);
        }

        [Fact]
        public void AutoLookupCommand_UsesSeriesAndEpisodeFromFolderPath()
        {
            // Suchanfrage muss Serienname + Folgentitel (ohne Laufnummer) enthalten
            // Query-Bau direkt testen (static Methode, kein LoadFolder nötig)
            string query = TagManagerViewModel.BuildAutoLookupQuery(
                @"D:\Hörspiele\Fünf Freunde\005 - Fünf Freunde auf der Felseninsel");

            Assert.Equal("Fünf Freunde Fünf Freunde auf der Felseninsel", query);
        }

        // ── ApplyLookupResult → HasPendingBatchTag ─────────────────────────────

        [Fact]
        public async Task ApplyLookupResult_SetsPendingBatchTag()
        {
            // Nach ApplyLookupResult soll "Alle taggen" verfügbar sein
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag())
            ];

            FakeTagService tagService = new(folderFiles);
            TagManagerViewModel vm = BuildViewModel(tagService);
            await vm.LoadFolderAsync(@"D:\test");

            Assert.False(vm.HasPendingBatchTag);

            vm.ApplyLookupResult(new TagLookupResult
            {
                Album = "Der Super-Papagei",
                Artist = "Die drei ???",
                Year = 1979,
                Genre = "Hörspiel",
                TrackCount = 8
            });

            Assert.True(vm.HasPendingBatchTag);
        }

        [Fact]
        public void ApplyLookupResult_SetsFormFields_FromLookupResult()
        {
            // ApplyLookupResult setzt die Formularfelder korrekt
            TagManagerViewModel vm = BuildViewModel();

            vm.ApplyLookupResult(new TagLookupResult
            {
                Title = "Testfolge",
                Album = "Testalbum",
                Artist = "Testkünstler",
                Year = 2023,
                Genre = "Hörspiel",
                TrackCount = 12
            });

            Assert.Equal("Testfolge", vm.Title);
            Assert.Equal("Testalbum", vm.Album);
            Assert.Equal("Testkünstler", vm.Artist);
            Assert.Equal("Hörspiel", vm.Genre);
            Assert.Equal("2023", vm.Year);
            Assert.Equal("12", vm.TrackCount);
            Assert.True(vm.HasUnsavedChanges);
        }

        [Fact]
        public void ApplyLookupResult_KeepsExistingField_WhenResultFieldIsNull()
        {
            // Wenn das Lookup-Ergebnis ein Feld nicht hat, bleibt der bestehende Wert erhalten
            TagManagerViewModel vm = BuildViewModel();

            // Formularfeld vorher setzen (simuliert manuell eingetragenen Wert)
            vm.ApplyLookupResult(new TagLookupResult
            {
                Title = "Erster Titel",
                Genre = "Drama"
            });

            // Zweiter Lookup ohne Title/Genre → bestehende Werte bleiben
            vm.ApplyLookupResult(new TagLookupResult
            {
                Album = "Neues Album",
                Artist = "Neuer Interpret"
            });

            Assert.Equal("Erster Titel", vm.Title);
            Assert.Equal("Drama", vm.Genre);
            Assert.Equal("Neues Album", vm.Album);
            Assert.Equal("Neuer Interpret", vm.Artist);
        }

        // ── ApplyToAll / SaveAll / CoverToAll ────────────────────────────────────
        // Diese Methoden nutzen ResourceLoader.GetString() für Bestätigungsdialoge,
        // was nur im WinUI-Kontext funktioniert. Die Batch-Logik (MergeSharedIntoExisting)
        // wird über die ApplyLookupResult-Tests indirekt validiert.
        // Vollständige Integration wird als manueller Smoke-Test geprüft.

        // ── Mehrfachauswahl ──────────────────────────────────────────────────────

        [Fact]
        public async Task SetSelectedFiles_SingleFile_SetsSelectedFile()
        {
            // Bei Einzelauswahl wird SelectedFile gesetzt
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag { Title = "Kapitel 1" }),
                (@"D:\test\track2.mp3", new AudioTag { Title = "Kapitel 2" })
            ];

            FakeTagService tagService = new(folderFiles);
            TagManagerViewModel vm = BuildViewModel(tagService);
            await vm.LoadFolderAsync(@"D:\test");

            vm.SetSelectedFiles([vm.Files[0]]);
            await Task.Delay(100);

            Assert.NotNull(vm.SelectedFile);
            Assert.Equal(@"D:\test\track1.mp3", vm.SelectedFile!.FilePath);
            Assert.True(vm.HasSelectedFile);
        }

        [Fact]
        public async Task SetSelectedFiles_MultipleFiles_ShowsSharedFields()
        {
            // Bei Mehrfachauswahl: gleiche Felder anzeigen, unterschiedliche als Platzhalter
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag { Title = "Kapitel 1", Album = "Gleiches Album", Artist = "Gleicher Künstler" }),
                (@"D:\test\track2.mp3", new AudioTag { Title = "Kapitel 2", Album = "Gleiches Album", Artist = "Gleicher Künstler" })
            ];

            FakeTagService tagService = new(folderFiles);
            TagManagerViewModel vm = BuildViewModel(tagService);
            await vm.LoadFolderAsync(@"D:\test");

            vm.SetSelectedFiles([vm.Files[0], vm.Files[1]]);
            await Task.Delay(200);

            // Album und Artist sind gleich → werden angezeigt
            Assert.Equal("Gleiches Album", vm.Album);
            Assert.Equal("Gleicher Künstler", vm.Artist);

            // Title ist unterschiedlich → Platzhalter
            Assert.NotEqual("Kapitel 1", vm.Title);
            Assert.NotEqual("Kapitel 2", vm.Title);

            // SelectedFile ist null bei Mehrfachauswahl
            Assert.Null(vm.SelectedFile);
            Assert.True(vm.HasSelectedFile);
        }

        [Fact]
        public async Task SetSelectedFiles_Empty_ClearsFields()
        {
            IReadOnlyList<(string, AudioTag)> folderFiles =
            [
                (@"D:\test\track1.mp3", new AudioTag { Title = "Kapitel 1" })
            ];

            FakeTagService tagService = new(folderFiles);
            TagManagerViewModel vm = BuildViewModel(tagService);
            await vm.LoadFolderAsync(@"D:\test");

            // Erst auswählen, dann Auswahl leeren
            vm.SetSelectedFiles([vm.Files[0]]);
            await Task.Delay(100);
            vm.SetSelectedFiles([]);

            Assert.Null(vm.SelectedFile);
            Assert.False(vm.HasSelectedFile);
            Assert.Null(vm.Title);
        }

        // ── TagFileItemViewModel.RelativePath ────────────────────────────────────

        [Fact]
        public void TagFileItemViewModel_RelativePath_ShowsSubfolderPath()
        {
            TagFileItemViewModel vm = new(
                @"D:\Hörspiele\Die drei ???\001 - Der Super-Papagei\track01.mp3",
                @"D:\Hörspiele\Die drei ???");

            Assert.Equal(@"001 - Der Super-Papagei\track01.mp3", vm.RelativePath);
        }

        [Fact]
        public void TagFileItemViewModel_RelativePath_FallsBackToFileName_WhenNoBaseFolder()
        {
            TagFileItemViewModel vm = new(@"D:\test\track01.mp3");

            Assert.Equal("track01.mp3", vm.RelativePath);
        }

        /// <summary>
        /// Erstellt ein <see cref="TagManagerViewModel"/> mit minimalen Fakes.
        /// </summary>
        private static TagManagerViewModel BuildViewModel(
            FakeTagService? tagService       = null,
            FakeTagLookupService? lookupService = null,
            FakeOnlineAccessGuard? onlineGuard  = null)
        {
            return new TagManagerViewModel(
                tagService          ?? new FakeTagService(),
                lookupService       ?? new FakeTagLookupService(),
                new FakeFileRenameService(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                onlineGuard         ?? new FakeOnlineAccessGuard());
        }
    }
}
