using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Einstellungen der lokalen Mediathek: Übernehmen und Zurückschreiben der
    /// Werte, den Abgleich von Hand und das Erkennen des Ordnermusters.
    /// </summary>
    /// <remarks>
    /// Zwei Stellen tragen diese Klasse. Erstens das Sammelladen: Werte, die aus der
    /// Ablage kommen, dürfen die Schaltfläche „Speichern" nicht aufwecken — sonst sieht
    /// jeder Seitenwechsel nach ungesicherten Änderungen aus. Zweitens die Weiche beim
    /// Muster: Ein Vorschlag wird nur dann ungefragt übernommen, wenn er allein steht und
    /// sicher genug ist; sonst entscheidet der Anwender.
    /// </remarks>
    public sealed class LocalSettingsViewModelTests
    {
        private const string LibraryPath = @"D:\Media";

        /// <summary>Das Muster, das ab Werk im Feld steht — Vergleichswert für „unverändert".</summary>
        private const string DefaultPattern = "{number:000} - {title}";

        // ── Laden und Schreiben ──────────────────────────────────────────────────

        [Fact]
        public void LoadFrom_DoesNotLookLikeAUserEdit()
        {
            Harness harness = Harness.Build();

            harness.ViewModel.LoadFrom(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = LibraryPath,
                EpisodeFolderPattern = "{nr} {title}",
                AutoImportAfterScan = true,
            });

            // Käme hier ein Änderungssignal, stünde nach jedem Öffnen der Einstellungen
            // „ungespeicherte Änderungen" — und die Warnung beim Verlassen verlöre ihren Sinn.
            Assert.Equal(0, harness.UserEdits);
        }

        [Fact]
        public void LoadFrom_TakesEveryValue()
        {
            Harness harness = Harness.Build();

            harness.ViewModel.LoadFrom(new AppSettings
            {
                LocalLibraryEnabled = true,
                LocalLibraryRootPath = LibraryPath,
                EpisodeFolderPattern = "{nr} {title}",
                AutoImportAfterScan = true,
            });

            Assert.True(harness.ViewModel.LocalLibraryEnabled);
            Assert.Equal(LibraryPath, harness.ViewModel.LocalLibraryRootPath);
            Assert.Equal("{nr} {title}", harness.ViewModel.EpisodeFolderPattern);
            Assert.True(harness.ViewModel.AutoImportAfterScan);
        }

        [Fact]
        public void WriteTo_StoresAnEmptyPathAsNothing()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryRootPath = "   ";

            AppSettings settings = new();
            harness.ViewModel.WriteTo(settings);

            // Ein Pfad aus Leerzeichen ist kein Ordner. Bliebe er stehen, suchte der
            // nächste Lauf an einer Stelle, die es nicht gibt.
            Assert.Null(settings.LocalLibraryRootPath);
        }

        [Fact]
        public void WriteTo_KeepsAnEnteredPath()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            AppSettings settings = new();
            harness.ViewModel.WriteTo(settings);

            Assert.Equal(LibraryPath, settings.LocalLibraryRootPath);
        }

        [Fact]
        public void LoadFrom_WithoutSettings_Throws()
        {
            Harness harness = Harness.Build();

            _ = Assert.Throws<ArgumentNullException>(() => harness.ViewModel.LoadFrom(null!));
        }

        [Fact]
        public void WriteTo_WithoutSettings_Throws()
        {
            Harness harness = Harness.Build();

            _ = Assert.Throws<ArgumentNullException>(() => harness.ViewModel.WriteTo(null!));
        }

        [Theory]
        [InlineData("enabled")]
        [InlineData("path")]
        [InlineData("pattern")]
        [InlineData("autoimport")]
        public void ChangingAValue_MarksTheSettingsAsEdited(string field)
        {
            Harness harness = Harness.Build();

            switch (field)
            {
                // Die Bibliothek ist ab Werk an — die Änderung ist hier das Abschalten.
                case "enabled": harness.ViewModel.LocalLibraryEnabled = false; break;
                case "path": harness.ViewModel.LocalLibraryRootPath = LibraryPath; break;
                case "pattern": harness.ViewModel.EpisodeFolderPattern = "{nr}"; break;
                default: harness.ViewModel.AutoImportAfterScan = true; break;
            }

            Assert.Equal(1, harness.UserEdits);
        }

        // ── Abgleich von Hand ────────────────────────────────────────────────────

        [Fact]
        public void SyncButton_StaysOffWithoutAPath()
        {
            Harness harness = Harness.Build();

            Assert.False(harness.ViewModel.IsSyncEnabled);
        }

        [Fact]
        public void SyncButton_TurnsOnWithAPath()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            Assert.True(harness.ViewModel.IsSyncEnabled);
        }

        [Fact]
        public async Task Sync_WithTheLibraryTurnedOff_SaysSoAndStops()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;
            harness.ViewModel.LocalLibraryEnabled = false;

            await harness.ViewModel.SyncAsync();

            // Der Dienst gäbe hier ein leeres Ergebnis zurück, und der Anwender sähe
            // „0 Titel angelegt" statt des Grundes.
            Assert.Equal(0, harness.SyncService.SyncCallCount);
            Assert.Equal("Lokale Bibliothek ist deaktiviert.", harness.ViewModel.SyncStatusText);
        }

        [Fact]
        public async Task Sync_WithoutAPath_SaysSoAndStops()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryEnabled = true;

            await harness.ViewModel.SyncAsync();

            Assert.Equal(0, harness.SyncService.SyncCallCount);
            Assert.Equal("Kein Bibliotheksordner konfiguriert.", harness.ViewModel.SyncStatusText);
        }

        [Fact]
        public async Task Sync_WhenReady_RunsAndReportsTheResult()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryEnabled = true;
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            await harness.ViewModel.SyncAsync();

            Assert.Equal(1, harness.SyncService.SyncCallCount);
            Assert.False(harness.ViewModel.IsSyncing);
            Assert.True(harness.ViewModel.IsSyncEnabled);
        }

        [Fact]
        public async Task Sync_WhenItFails_ShowsTheReasonAndFreesTheButton()
        {
            Harness harness = Harness.Build(syncFailure: new InvalidOperationException("Laufwerk fehlt"));
            harness.ViewModel.LocalLibraryEnabled = true;
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            await harness.ViewModel.SyncAsync();

            // Bliebe die Schaltfläche gesperrt, käme der Anwender nach einem einzigen
            // Fehlschlag nicht mehr an den Abgleich heran.
            Assert.Contains("Laufwerk fehlt", harness.ViewModel.SyncStatusText, StringComparison.Ordinal);
            Assert.True(harness.ViewModel.IsSyncEnabled);
            _ = Assert.Single(harness.ErrorDialog.ShownDialogs);
        }

        // ── Muster erkennen ──────────────────────────────────────────────────────

        [Fact]
        public async Task AnalyzePattern_WithoutAPath_LooksAtNothing()
        {
            Harness harness = Harness.Build(suggestions: [Suggestion("{nr} {title}", 0.95)]);

            await harness.ViewModel.AnalyzePatternAsync();

            Assert.Null(harness.Analyzer.LastFolderPath);
            Assert.Empty(harness.ViewModel.PatternSuggestions);
        }

        [Fact]
        public async Task AnalyzePattern_WithOneConfidentHit_TakesItWithoutAsking()
        {
            Harness harness = Harness.Build(suggestions: [Suggestion("{nr} {title}", 0.95)]);
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            bool asked = false;
            harness.ViewModel.PatternSelectionRequested += _ => asked = true;

            await harness.ViewModel.AnalyzePatternAsync();

            // Ein einziger Vorschlag, der auf fünfundneunzig von hundert Ordnern passt,
            // braucht keine Rückfrage — sie wäre nur ein Klick ohne Alternative.
            Assert.Equal("{nr} {title}", harness.ViewModel.EpisodeFolderPattern);
            Assert.False(asked);
        }

        [Fact]
        public async Task AnalyzePattern_WithOneUncertainHit_AsksTheUser()
        {
            Harness harness = Harness.Build(suggestions: [Suggestion("{nr} {title}", 0.60)]);
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            List<PatternSuggestionDisplay> offered = [];
            harness.ViewModel.PatternSelectionRequested += displays => offered.AddRange(displays);

            await harness.ViewModel.AnalyzePatternAsync();

            // Sechzig von hundert heißt: vierzig Ordner heißen anders. Das ungefragt zu
            // übernehmen benennt beim nächsten Lauf die falschen Ordner um.
            _ = Assert.Single(offered);
            Assert.Equal(DefaultPattern, harness.ViewModel.EpisodeFolderPattern);
        }

        [Fact]
        public async Task AnalyzePattern_WithSeveralHits_AsksTheUser()
        {
            Harness harness = Harness.Build(suggestions:
            [
                Suggestion("{nr} {title}", 0.95),
                Suggestion("{nr}. {title}", 0.90),
            ]);
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            List<PatternSuggestionDisplay> offered = [];
            harness.ViewModel.PatternSelectionRequested += displays => offered.AddRange(displays);

            await harness.ViewModel.AnalyzePatternAsync();

            // Auch wenn beide sicher aussehen: Nur einer kann gelten, und die Wahl gehört
            // dem Anwender.
            Assert.Equal(2, offered.Count);
            Assert.Equal(DefaultPattern, harness.ViewModel.EpisodeFolderPattern);
        }

        [Fact]
        public async Task AnalyzePattern_WithoutAnyHit_AsksNothing()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            bool asked = false;
            harness.ViewModel.PatternSelectionRequested += _ => asked = true;

            await harness.ViewModel.AnalyzePatternAsync();

            // Ein leerer Auswahldialog wäre eine Sackgasse.
            Assert.False(asked);
            Assert.Equal(Visibility.Collapsed, harness.ViewModel.PatternSuggestionsVisibility);
        }

        [Fact]
        public async Task AnalyzePattern_ShowsTheSuggestionsInTheView()
        {
            Harness harness = Harness.Build(suggestions:
            [
                Suggestion("{nr} {title}", 0.70),
                Suggestion("{nr}. {title}", 0.60),
            ]);
            harness.ViewModel.LocalLibraryRootPath = LibraryPath;

            await harness.ViewModel.AnalyzePatternAsync();

            Assert.Equal(Visibility.Visible, harness.ViewModel.PatternSuggestionsVisibility);
        }

        [Fact]
        public void ApplyPatternSuggestion_TakesTheChosenPattern()
        {
            Harness harness = Harness.Build();

            harness.ViewModel.ApplyPatternSuggestion("{nr} - {title}");

            Assert.Equal("{nr} - {title}", harness.ViewModel.EpisodeFolderPattern);
            Assert.Equal(1, harness.UserEdits);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static PatternSuggestion Suggestion(string pattern, double matchPercentage) =>
            new(pattern, MatchCount: 42, matchPercentage);

        /// <summary>Baut das Ansichtsmodell samt Umfeld und zählt die Änderungssignale.</summary>
        private sealed class Harness
        {
            public LocalSettingsViewModel ViewModel { get; private set; } = null!;

            public required FakeSyncService SyncService { get; init; }

            public required FakeEpisodePatternAnalyzer Analyzer { get; init; }

            public required FakeErrorDialogService ErrorDialog { get; init; }

            public int UserEdits { get; private set; }

            public static Harness Build(
                IReadOnlyList<PatternSuggestion>? suggestions = null,
                Exception? syncFailure = null)
            {
                FakeSyncService syncService = new(new SyncResult(), syncFailure);
                FakeEpisodePatternAnalyzer analyzer = new(suggestions);
                FakeErrorDialogService errorDialog = new();

                Harness harness = new()
                {
                    SyncService = syncService,
                    Analyzer = analyzer,
                    ErrorDialog = errorDialog,
                };

                harness.ViewModel = new LocalSettingsViewModel(
                    syncService, errorDialog, analyzer, () => harness.UserEdits++);

                return harness;
            }
        }
    }
}
