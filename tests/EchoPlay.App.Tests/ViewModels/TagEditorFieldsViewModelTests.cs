using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Models;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, was aus den Eingabefeldern des Tag-Managers beim Speichern in die Dateien geht.
    /// </summary>
    /// <remarks>
    /// Bei Mehrfachauswahl zeigt ein Feld mit unterschiedlichen Werten einen Platzhalter.
    /// Gelangt er in einen der Bausteine für das Speichern, steht er danach als Album oder
    /// Genre in jeder Datei, und das lässt sich nicht rückgängig machen.
    /// </remarks>
    public sealed class TagEditorFieldsViewModelTests
    {
        [Fact]
        public void BuildSharedTag_AfterMixedSelection_LeavesThePlaceholderOut()
        {
            TagEditorFieldsViewModel sut = new(() => { });
            sut.PopulateFromMultipleTags(
            [
                new AudioTag { Album = "Folge 1", Genre = "Hörspiel", Artist = "TKKG" },
                new AudioTag { Album = "Folge 2", Genre = "Krimi", Artist = "TKKG" },
            ]);

            AudioTag shared = sut.BuildSharedTagFromFields();

            Assert.Equal(TagEditorFieldsViewModel.MixedValuePlaceholder, sut.Album);
            Assert.Null(shared.Album);
            Assert.Null(shared.Genre);
            Assert.Equal("TKKG", shared.Artist);
        }

        [Fact]
        public void Setter_WithUnchangedValue_DoesNotMarkTheField()
        {
            int edits = 0;
            TagEditorFieldsViewModel sut = new(() => edits++);
            sut.PopulateFromMultipleTags(
            [
                new AudioTag { Album = "Folge 1" },
                new AudioTag { Album = "Folge 2" },
            ]);

            // So schreibt eine Bindung beim Verlassen des Felds den angezeigten Text zurück.
            sut.Album = sut.Album;

            Assert.Equal(0, edits);
            Assert.Equal(0, sut.EditedFieldCount);
            Assert.Null(sut.BuildEditedFieldsTag().Album);
        }

        [Fact]
        public void Setter_WithNewValue_MarksOnlyThatField()
        {
            int edits = 0;
            TagEditorFieldsViewModel sut = new(() => edits++);
            sut.PopulateFromMultipleTags(
            [
                new AudioTag { Album = "Folge 1", Genre = "Hörspiel" },
                new AudioTag { Album = "Folge 2", Genre = "Hörspiel" },
            ]);

            sut.Album = "Sammelband";

            Assert.Equal(1, edits);
            AudioTag edited = sut.BuildEditedFieldsTag();
            Assert.Equal("Sammelband", edited.Album);
            Assert.Null(edited.Genre);
        }
    }
}
