using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Online-Abfrage im Tag-Manager: manuelle Suche, automatische Suche aus dem
    /// Ordnernamen und das Übertragen eines Treffers auf alle Dateien.
    /// </summary>
    /// <remarks>
    /// Zwei Zusagen tragen diese Klasse. Erstens: Ohne Netzfreigabe geht keine Anfrage
    /// hinaus — „nur offline" ist eine Nutzerentscheidung, keine Voreinstellung, die eine
    /// Hintergrundabfrage übergehen darf. Zweitens: Ein Treffer wird nur dann ungefragt
    /// übernommen, wenn seine Titelzahl zum Ordner passt; sonst entscheidet der Anwender.
    /// </remarks>
    public sealed class TagLookupActionsTests
    {
        private const string FolderPath = @"D:\Media\Die drei Fragezeichen\001 Der Super-Papagei";

        // ── Manuelle Suche ───────────────────────────────────────────────────────

        [Fact]
        public async Task LookupOnline_WithoutSelection_AsksNothing()
        {
            Harness harness = Harness.Build();

            await harness.Actions.LookupOnlineAsync();

            Assert.Equal(0, harness.OnlineGuard.CallCount);
            Assert.Null(harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task LookupOnline_WhenOnlineAccessIsDeclined_SendsNoRequest()
        {
            Harness harness = Harness.Build(allowOnlineAccess: false);
            harness.SelectFirstFile();

            await harness.Actions.LookupOnlineAsync();

            // „Nur offline" ist eine Entscheidung des Anwenders. Eine Abfrage, die sie
            // übergeht, verrät seinen Bestand an einen fremden Dienst.
            Assert.Equal(1, harness.OnlineGuard.CallCount);
            Assert.Null(harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task LookupOnline_UsesTheTitleFromTheEditor()
        {
            Harness harness = Harness.Build();
            harness.SelectFirstFile();
            harness.Editor.Title = "Der Super-Papagei";

            await harness.Actions.LookupOnlineAsync();

            Assert.Equal("Der Super-Papagei", harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task LookupOnline_WithMixedTitles_FallsBackToTheFileName()
        {
            Harness harness = Harness.Build();
            harness.SelectFirstFile();
            harness.Editor.Title = "(verschieden)";

            await harness.Actions.LookupOnlineAsync();

            // Bei Mehrfachauswahl steht „(verschieden)" im Feld. Damit zu suchen fände
            // nichts — der Dateiname ist der brauchbare Rest.
            Assert.Equal("track1", harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task LookupOnline_HandsTheResultsToTheView()
        {
            Harness harness = Harness.Build(results: [Result("Der Super-Papagei", trackCount: 5)]);
            harness.SelectFirstFile();

            IReadOnlyList<TagLookupCandidate>? delivered = null;
            harness.Actions.LookupResultsReady += (_, candidates) => delivered = candidates;

            await harness.Actions.LookupOnlineAsync();

            Assert.NotNull(delivered);
            Assert.Equal("Der Super-Papagei", Assert.Single(delivered).Title);
        }

        [Fact]
        public async Task LookupOnline_WhenTheRequestFails_ShowsAnErrorAndStops()
        {
            Harness harness = Harness.Build(searchFailure: new HttpRequestException("Zeitüberschreitung"));
            harness.SelectFirstFile();

            await harness.Actions.LookupOnlineAsync();

            (string Title, string Message) shown = Assert.Single(harness.ErrorDialog.ShownDialogs);
            Assert.Equal("Zeitüberschreitung", shown.Message);
            Assert.Equal([true, false], harness.LookingUpStates);
        }

        // ── Automatische Suche ───────────────────────────────────────────────────

        [Fact]
        public async Task AutoLookup_WithoutFolder_SendsNoRequest()
        {
            Harness harness = Harness.Build();

            await harness.Actions.AutoLookupAsync();

            // Ohne Ordner gibt es keinen Suchbegriff. Eine leere Anfrage kostet nur Zeit
            // und liefert Treffer, die zu nichts passen.
            Assert.Equal(0, harness.OnlineGuard.CallCount);
            Assert.Null(harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task AutoLookup_BuildsTheQueryFromSeriesAndEpisodeFolder()
        {
            Harness harness = Harness.Build();
            harness.OpenFolder();

            await harness.Actions.AutoLookupAsync();

            // Der übergeordnete Ordner ist die Serie, der aktuelle die Folge — ohne die
            // führende Laufnummer, die kein Anbieter kennt.
            Assert.Equal("Die drei Fragezeichen Der Super-Papagei", harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task AutoLookup_WhenOnlineAccessIsDeclined_SendsNoRequest()
        {
            Harness harness = Harness.Build(allowOnlineAccess: false);
            harness.OpenFolder();

            await harness.Actions.AutoLookupAsync();

            Assert.Equal(1, harness.OnlineGuard.CallCount);
            Assert.Null(harness.LookupService.LastQuery);
        }

        [Fact]
        public async Task AutoLookup_WithMatchingTrackCount_AppliesTheHitDirectly()
        {
            Harness harness = Harness.Build(results: [Result("Der Super-Papagei", trackCount: 2)]);
            harness.OpenFolder();

            TagLookupCandidate? applied = null;
            harness.Actions.AutoLookupApplied += (_, candidate) => applied = candidate;

            await harness.Actions.AutoLookupAsync();

            // Stimmt die Titelzahl mit dem Ordner überein, ist der Treffer eindeutig genug,
            // um ihn ohne Rückfrage zu übernehmen.
            Assert.NotNull(applied);
            Assert.Equal("Der Super-Papagei", harness.Editor.Title);
        }

        [Fact]
        public async Task AutoLookup_WithDifferentTrackCount_LetsTheUserChoose()
        {
            Harness harness = Harness.Build(results: [Result("Fremde Folge", trackCount: 17)]);
            harness.OpenFolder();

            bool appliedDirectly = false;
            IReadOnlyList<TagLookupCandidate>? offered = null;
            harness.Actions.AutoLookupApplied += (_, _) => appliedDirectly = true;
            harness.Actions.LookupResultsReady += (_, candidates) => offered = candidates;

            await harness.Actions.AutoLookupAsync();

            // Siebzehn Titel gegen zwei Dateien: Das ist nicht dieselbe Folge. Würde die
            // Anwendung das übernehmen, stünde in jeder Datei ein fremdes Album.
            Assert.False(appliedDirectly);
            Assert.NotNull(offered);
            Assert.True(string.IsNullOrEmpty(harness.Editor.Title));
        }

        [Fact]
        public async Task AutoLookup_OffersTheClosestHitFirst()
        {
            Harness harness = Harness.Build(results:
            [
                Result("Ohne Angabe", trackCount: null),
                Result("Fremde Folge", trackCount: 17),
                Result("Passende Folge", trackCount: 2),
            ]);
            harness.OpenFolder();

            IReadOnlyList<TagLookupCandidate>? offered = null;
            harness.Actions.LookupResultsReady += (_, candidates) => offered = candidates;

            // Zwei Dateien liegen im Ordner, aber der beste Treffer steht nicht an erster
            // Stelle. Trifft die Vorauswahl, wird direkt übernommen — deshalb prüft dieser
            // Test die Reihenfolge über einen Ordner mit drei Dateien.
            harness.OpenFolderWithThreeFiles();

            await harness.Actions.AutoLookupAsync();

            Assert.NotNull(offered);
            Assert.Equal("Fremde Folge", offered[0].Title);
            Assert.Equal("Ohne Angabe", offered[^1].Title);
        }

        [Fact]
        public async Task AutoLookup_WhenTheRequestFails_ClearsTheStatusLine()
        {
            Harness harness = Harness.Build(searchFailure: new HttpRequestException("Kein Netz"));
            harness.OpenFolder();

            await harness.Actions.AutoLookupAsync();

            // Bliebe „Suche läuft …" stehen, wartete der Anwender auf ein Ergebnis, das
            // nie kommt.
            Assert.Equal(string.Empty, harness.AutoLookupStatus);
            (string Title, string Message) shown = Assert.Single(harness.ErrorDialog.ShownDialogs);
            Assert.Equal("Kein Netz", shown.Message);
        }

        [Fact]
        public async Task AutoLookup_AlwaysCompletesItsWaitHandle()
        {
            Harness harness = Harness.Build(searchFailure: new HttpRequestException("Kein Netz"));
            harness.OpenFolder();

            await harness.Actions.AutoLookupAsync();

            // Der Abschluss meldet sich auch im Fehlerfall. Ohne das hinge jeder Aufrufer,
            // der darauf wartet, unbegrenzt fest.
            await harness.Actions.WaitForAutoLookupCompleteAsync();
        }

        // ── Auf alle Dateien anwenden ────────────────────────────────────────────

        [Fact]
        public async Task ApplyToAll_WithoutAPreviousHit_WritesNothing()
        {
            Harness harness = Harness.Build();
            harness.OpenFolder();

            await harness.Actions.ApplyToAllAsync();

            Assert.Equal(0, harness.TagService.WriteCallCount);
        }

        [Fact]
        public async Task ApplyToAll_WhenUserDeclines_WritesNothing()
        {
            Harness harness = Harness.Build(confirmResult: false);
            harness.OpenFolder();
            harness.Actions.ApplyLookupResult(Result("Der Super-Papagei", trackCount: 2));

            await harness.Actions.ApplyToAllAsync();

            // Der Vorgang überschreibt Album, Künstler, Jahr und Genre in jeder Datei des
            // Ordners. Eine Ablehnung muss jede davon unberührt lassen.
            Assert.Equal(1, harness.Confirmation.CallCount);
            Assert.Equal(0, harness.TagService.WriteCallCount);
        }

        [Fact]
        public async Task ApplyToAll_WhenConfirmed_KeepsTitleAndTrackNumber()
        {
            Harness harness = Harness.Build();
            harness.OpenFolder();
            harness.Actions.ApplyLookupResult(Result("Der Super-Papagei", trackCount: 2, album: "Folge 1"));

            await harness.Actions.ApplyToAllAsync();

            // Album und Künstler sind für den ganzen Ordner gleich, Titel und Titelnummer
            // gehören zur einzelnen Datei. Würden sie mitüberschrieben, hießen alle Dateien
            // gleich — und die Reihenfolge wäre verloren.
            AudioTag? written = harness.TagService.GetWrittenTag($@"{FolderPath}\track1.mp3");
            Assert.Equal("Folge 1", written?.Album);
            Assert.Equal("Kapitel 1", written?.Title);
        }

        [Fact]
        public async Task ApplyToAll_WhenConfirmed_ClearsThePendingHit()
        {
            Harness harness = Harness.Build();
            harness.OpenFolder();
            harness.Actions.ApplyLookupResult(Result("Der Super-Papagei", trackCount: 2));

            await harness.Actions.ApplyToAllAsync();

            // Der Treffer ist verbraucht. Bliebe er stehen, schriebe der nächste Klick ihn
            // ein zweites Mal in einen womöglich anderen Ordner.
            Assert.Null(harness.Editor.PendingBatchTag);
        }

        [Fact]
        public async Task ApplyToAll_WithRenamePattern_RefreshesThePreview()
        {
            Harness harness = Harness.Build();
            harness.OpenFolder();
            harness.Rename.RenamePattern = "{track} - {title}";
            harness.Actions.ApplyLookupResult(Result("Der Super-Papagei", trackCount: 2));

            bool previewReady = false;
            harness.Actions.RenamePreviewReady += (_, _) => previewReady = true;

            await harness.Actions.ApplyToAllAsync();

            // Die Vorschau baut auf den Kennzeichnungen auf. Nach dem Überschreiben zeigte
            // sie sonst Namen aus den alten Werten.
            Assert.True(harness.PreviewRefreshed);
            Assert.True(previewReady);
        }

        [Fact]
        public async Task ApplyToAll_WithClearedRenamePattern_LeavesThePreviewAlone()
        {
            Harness harness = Harness.Build();
            harness.OpenFolder();
            harness.Rename.RenamePattern = string.Empty;
            harness.Actions.ApplyLookupResult(Result("Der Super-Papagei", trackCount: 2));

            await harness.Actions.ApplyToAllAsync();

            // Ein Muster steht ab Werk im Feld; leer ist es nur, wenn der Anwender es
            // gelöscht hat. Dann will er nicht umbenennen — und bekommt auch keine
            // Vorschau untergeschoben.
            Assert.False(harness.PreviewRefreshed);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static TagLookupResult Result(string title, uint? trackCount, string album = "Album") =>
            new()
            {
                Title = title,
                Artist = "Europa",
                Album = album,
                Year = 1979,
                TrackCount = trackCount,
                Source = "Test",
            };

        /// <summary>
        /// Baut die Aktionen samt Umfeld. Zusammengefasst, weil die Tests auf mehrere
        /// Nachbauten und auf vier der Rückrufe zugreifen.
        /// </summary>
        private sealed class Harness
        {
            public TagLookupActions Actions { get; private set; } = null!;

            public required TagFileListViewModel FileList { get; init; }

            public required TagEditorFieldsViewModel Editor { get; init; }

            public required TagRenameViewModel Rename { get; init; }

            public required FakeTagService TagService { get; init; }

            public required FakeTagLookupService LookupService { get; init; }

            public required FakeOnlineAccessGuard OnlineGuard { get; init; }

            public required FakeErrorDialogService ErrorDialog { get; init; }

            public required FakeConfirmationDialogService Confirmation { get; init; }

            public List<bool> LookingUpStates { get; } = [];

            public string AutoLookupStatus { get; private set; } = string.Empty;

            public bool PreviewRefreshed { get; private set; }

            /// <summary>Öffnet einen Ordner mit zwei Dateien.</summary>
            public void OpenFolder() => SetFiles(2);

            /// <summary>Öffnet einen Ordner mit drei Dateien — für den Fall ohne Volltreffer.</summary>
            public void OpenFolderWithThreeFiles() => SetFiles(3);

            /// <summary>Wählt die erste Datei aus, wie es ein Klick in der Liste tut.</summary>
            public void SelectFirstFile()
            {
                SetFiles(2);
                FileList.SetSelectedFiles([FileList.Files[0]]);
            }

            private void SetFiles(int count)
            {
                List<TagFileItemViewModel> files = new(count);
                for (int i = 1; i <= count; i++)
                {
                    files.Add(new TagFileItemViewModel($@"{FolderPath}\track{i}.mp3", FolderPath));
                }

                FileList.SetFiles(files, FolderPath);
            }

            public static Harness Build(
                IReadOnlyList<TagLookupResult>? results = null,
                bool allowOnlineAccess = true,
                bool confirmResult = true,
                Exception? searchFailure = null)
            {
                FakeTagLookupService lookupService = new(results, searchFailure);
                FakeOnlineAccessGuard onlineGuard = new(allowOnlineAccess);
                FakeErrorDialogService errorDialog = new();
                FakeConfirmationDialogService confirmation = new(confirmResult);
                FakeTagService tagService = new(
                [
                    ($@"{FolderPath}\track1.mp3", new AudioTag { Title = "Kapitel 1", TrackNumber = 1 }),
                    ($@"{FolderPath}\track2.mp3", new AudioTag { Title = "Kapitel 2", TrackNumber = 2 }),
                    ($@"{FolderPath}\track3.mp3", new AudioTag { Title = "Kapitel 3", TrackNumber = 3 }),
                ]);

                ServiceCollection services = new();
                _ = services.AddScoped<ITagLookupService>(_ => lookupService);
                ServiceProvider provider = services.BuildServiceProvider();

                TagManagerActionsContext context = new(
                    TagService: tagService,
                    LookupCoordinator: new TagLookupCoordinator(
                        provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory()),
                    FileRenameService: new FakeFileRenameService(),
                    ErrorDialogService: errorDialog,
                    ConfirmationDialogService: confirmation,
                    OnlineAccessGuard: onlineGuard);

                Harness harness = new()
                {
                    FileList = new TagFileListViewModel(),
                    Editor = new TagEditorFieldsViewModel(() => { }),
                    Rename = new TagRenameViewModel(),
                    TagService = tagService,
                    LookupService = lookupService,
                    OnlineGuard = onlineGuard,
                    ErrorDialog = errorDialog,
                    Confirmation = confirmation,
                };

                harness.Actions = new TagLookupActions(
                    context,
                    harness.FileList,
                    harness.Editor,
                    harness.Rename,
                    setIsLoading: _ => { },
                    setIsLookingUp: state => harness.LookingUpStates.Add(state),
                    setAutoLookupStatus: status => harness.AutoLookupStatus = status,
                    setBatchProgress: _ => { },
                    setHasUnsavedChanges: _ => { },
                    refreshCommandStates: () => { },
                    previewRenameAsync: () =>
                    {
                        harness.PreviewRefreshed = true;
                        return Task.CompletedTask;
                    });

                return harness;
            }
        }
    }
}
