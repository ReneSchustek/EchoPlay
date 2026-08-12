using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Sichert Suche und Änderungsfilter der Dateiliste im Tag-Manager ab.
    /// </summary>
    public sealed class TagFileListFilterTests
    {
        private const string Ordner = @"C:\Hörspiele\Bibi";

        private static TagFileListViewModel BuildList()
        {
            TagFileListViewModel list = new();

            list.SetFiles(
                [
                    new TagFileItemViewModel($@"{Ordner}\01 Hexerei.mp3", Ordner),
                    new TagFileItemViewModel($@"{Ordner}\02 Der Bericht.mp3", Ordner),
                    new TagFileItemViewModel($@"{Ordner}\03 Hexbesen.mp3", Ordner)
                ],
                Ordner);

            return list;
        }

        private static IEnumerable<string> NamesOf(TagFileListViewModel list) =>
            list.Files.Select(f => f.FileName);

        [Fact]
        public void SetFiles_ShowsEveryFile()
        {
            TagFileListViewModel list = BuildList();

            Assert.Equal(3, list.Files.Count);
            Assert.False(list.HasActiveFilter);
        }

        [Fact]
        public void SearchText_MatchesTheFileNameRegardlessOfCase()
        {
            TagFileListViewModel list = BuildList();

            list.SearchText = "hex";

            Assert.Equal(["01 Hexerei.mp3", "03 Hexbesen.mp3"], NamesOf(list));
        }

        /// <summary>
        /// Der Filter zeigt, was vor dem Sichern noch offen ist — bei einem Ordner mit
        /// hundert Dateien der einzige Weg, das zu überblicken.
        /// </summary>
        [Fact]
        public void ModifiedOnly_KeepsFilesWithUnsavedChanges()
        {
            TagFileListViewModel list = BuildList();
            list.Files[1].IsModified = true;

            list.ModifiedOnly = true;

            Assert.Equal(["02 Der Bericht.mp3"], NamesOf(list));
        }

        [Fact]
        public void SearchAndFilter_ApplyTogether()
        {
            TagFileListViewModel list = BuildList();
            list.Files[0].IsModified = true;
            list.Files[1].IsModified = true;

            list.SearchText = "hex";
            list.ModifiedOnly = true;

            Assert.Equal(["01 Hexerei.mp3"], NamesOf(list));
        }

        [Fact]
        public void FilterWithoutMatches_ShowsTheNoResultsHint()
        {
            TagFileListViewModel list = BuildList();

            list.SearchText = "gibt es nicht";

            Assert.Equal(Visibility.Visible, list.NoResultsVisibility);
            Assert.True(list.HasActiveFilter);
        }

        /// <summary>
        /// Ohne geöffneten Ordner bleibt der „Nichts gefunden"-Hinweis aus — dort ist nichts
        /// da, nicht nichts gefunden.
        /// </summary>
        [Fact]
        public void EmptyList_DoesNotShowTheNoResultsHint()
        {
            TagFileListViewModel list = new();

            list.SearchText = "irgendwas";

            Assert.Equal(Visibility.Collapsed, list.NoResultsVisibility);
        }

        [Fact]
        public void ResetFilters_BringsBackEveryFile()
        {
            TagFileListViewModel list = BuildList();
            list.SearchText = "hex";
            list.ModifiedOnly = true;

            list.ResetFilters();

            Assert.Equal(3, list.Files.Count);
            Assert.False(list.HasActiveFilter);
            Assert.Equal(string.Empty, list.SearchText);
            Assert.False(list.ModifiedOnly);
        }

        /// <summary>
        /// Ein neuer Ordner zeigt seine Dateien auch dann vollständig, wenn zuvor gesucht
        /// wurde — sonst wirkte der frisch geöffnete Ordner halb leer.
        /// </summary>
        [Fact]
        public void SetFiles_AfterASearch_AppliesTheFilterToTheNewFolder()
        {
            TagFileListViewModel list = BuildList();
            list.SearchText = "hex";

            list.SetFiles([new TagFileItemViewModel($@"{Ordner}\04 Ausflug.mp3", Ordner)], Ordner);

            Assert.Empty(list.Files);
            Assert.Equal(Visibility.Visible, list.NoResultsVisibility);
        }

        [Fact]
        public void Clear_EmptiesTheListForGood()
        {
            TagFileListViewModel list = BuildList();

            list.Clear();
            list.SearchText = "hex";
            list.SearchText = string.Empty;

            Assert.Empty(list.Files);
        }
    }
}
