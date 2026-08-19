using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, was eine Folgenkachel der Online-Mediathek anzeigt: Beschriftung,
    /// Erscheinungsdatum, Gehört-Häkchen und die Schaltfläche zum Anbieter.
    /// </summary>
    /// <remarks>
    /// Der Erfolgsweg von „Im Browser öffnen" bleibt außen vor: Er startet eine fremde
    /// Anwendung. Geprüft ist der Weg davor — die Kachel darf sich nicht als gehört
    /// eintragen, wenn die Adresse gar nicht geöffnet wurde.
    /// </remarks>
    public sealed class OnlineEpisodeCardStateTests
    {
        [Fact]
        public void DisplayText_ShowsTheTitleAlone()
        {
            // Online eingelesene Folgen tragen die Nummer bereits im Titel; die
            // Nummer aus der Datenbank ist die Spur im Album und gehört nicht davor.
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 4, title: "125 - Der Karpatenhund");

            Assert.Equal("125 - Der Karpatenhund", card.DisplayText);
        }

        [Fact]
        public void ReleaseDateText_WithDate_ShowsItInDayMonthYear()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1",
                releaseDate: new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("07.03.2026", card.ReleaseDateText);
        }

        [Fact]
        public void ReleaseDateText_WithoutDate_StaysEmpty()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1");

            Assert.Equal(string.Empty, card.ReleaseDateText);
        }

        [Fact]
        public void OpenInBrowserVisibility_WithProviderUrl_ShowsTheButton()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1",
                providerUrl: "https://example.invalid/album/1");

            Assert.Equal(Visibility.Visible, card.OpenInBrowserVisibility);
        }

        [Fact]
        public void OpenInBrowserVisibility_WithoutProviderUrl_HidesTheButton()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1");

            Assert.Equal(Visibility.Collapsed, card.OpenInBrowserVisibility);
        }

        [Fact]
        public void CompletedVisibility_FollowsTheCompletedFlag()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1", isCompleted: true);

            Assert.Equal(Visibility.Visible, card.CompletedVisibility);

            card.IsCompleted = false;

            Assert.Equal(Visibility.Collapsed, card.CompletedVisibility);
        }

        [Fact]
        public void IsCompleted_WhenChanged_AnnouncesTheCheckmarkAsWell()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1");
            List<string?> changed = [];
            ((INotifyPropertyChanged)card).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            card.IsCompleted = true;

            // Ohne die zweite Meldung bliebe das Häkchen unsichtbar, bis die Liste
            // aus einem anderen Grund neu gezeichnet wird.
            Assert.Contains(nameof(OnlineEpisodeCardViewModel.IsCompleted), changed);
            Assert.Contains(nameof(OnlineEpisodeCardViewModel.CompletedVisibility), changed);
        }

        [Fact]
        public void IsCompleted_SetToTheSameValue_AnnouncesNothing()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1", isCompleted: true);
            List<string?> changed = [];
            ((INotifyPropertyChanged)card).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            card.IsCompleted = true;

            Assert.Empty(changed);
        }

        [Fact]
        public void OpenInBrowserCommand_WithoutProviderUrl_DoesNotMarkTheEpisodeAsHeard()
        {
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1");

            card.OpenInBrowserCommand.Execute(null);

            Assert.False(card.IsCompleted);
        }

        [Fact]
        public void OpenInBrowserCommand_WithANonWebAddress_DoesNotMarkTheEpisodeAsHeard()
        {
            // Nur http und https gehen an den Browser. Eine Datei- oder Eigenprotokoll-
            // Adresse würde sonst ein beliebiges Programm starten — und die Folge wäre
            // obendrein als gehört vermerkt, ohne dass etwas passiert ist.
            OnlineEpisodeCardViewModel card = new(
                TestIds.EpisodeA, episodeNumber: 1, title: "Folge 1",
                providerUrl: "file:///C:/Windows/System32/calc.exe");

            card.OpenInBrowserCommand.Execute(null);

            Assert.False(card.IsCompleted);
        }
    }
}
