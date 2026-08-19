using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.ComponentModel;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Neuerscheinungs-Abschnitt der Übersicht: wann er überhaupt erscheint,
    /// wann der Ladehinweis steht und wann die Gruppen sichtbar werden.
    /// </summary>
    /// <remarks>
    /// Die Abfrage beim Anbieter dauert bei vielen Serien Minuten. Ohne den Ladehinweis
    /// sähe der Anwender in dieser Zeit einen leeren Bereich und hielte ihn für kaputt —
    /// und ein Abschnitt, der ohne Inhalt und ohne Abfrage stehen bleibt, ist Ballast.
    /// </remarks>
    public sealed class DashboardNeuerscheinungenViewModelTests
    {
        [Fact]
        public void NewReleasesSection_WithoutGroupsAndWithoutLoading_StaysHidden()
        {
            DashboardNeuerscheinungenViewModel sut = new();

            Assert.Equal(Visibility.Collapsed, sut.NewReleasesSectionVisibility);
            Assert.Equal(Visibility.Collapsed, sut.NewEpisodeGroupsVisibility);
            Assert.Equal(Visibility.Collapsed, sut.NewReleasesLoadingVisibility);
        }

        [Fact]
        public void NewReleasesSection_WhileLoadingWithoutGroups_ShowsTheLoadingHint()
        {
            DashboardNeuerscheinungenViewModel sut = new()
            {
                IsLoadingNewReleases = true,
            };

            Assert.Equal(Visibility.Visible, sut.NewReleasesSectionVisibility);
            Assert.Equal(Visibility.Visible, sut.NewReleasesLoadingVisibility);
            Assert.Equal(Visibility.Collapsed, sut.NewEpisodeGroupsVisibility);
        }

        [Fact]
        public void SetGroups_WithEntries_ShowsTheGroupsAndTheSection()
        {
            DashboardNeuerscheinungenViewModel sut = new();

            sut.SetGroups([BuildGroup("Januar 2026")]);

            Assert.Equal(Visibility.Visible, sut.NewEpisodeGroupsVisibility);
            Assert.Equal(Visibility.Visible, sut.NewReleasesSectionVisibility);
            _ = Assert.Single(sut.NewEpisodeGroups);
        }

        [Fact]
        public void SetGroups_WhileStillLoading_HidesTheLoadingHint()
        {
            DashboardNeuerscheinungenViewModel sut = new()
            {
                IsLoadingNewReleases = true,
            };

            sut.SetGroups([BuildGroup("Januar 2026")]);

            // Sobald die ersten Gruppen da sind, ist der Hinweis überflüssig — er stünde
            // sonst über einer bereits gefüllten Liste.
            Assert.Equal(Visibility.Collapsed, sut.NewReleasesLoadingVisibility);
        }

        [Fact]
        public void SetGroups_AnnouncesEveryDependentVisibility()
        {
            DashboardNeuerscheinungenViewModel sut = new();
            List<string?> changed = [];
            ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            sut.SetGroups([BuildGroup("Februar 2026")]);

            // Ohne diese Meldungen bliebe der Abschnitt unsichtbar, obwohl er Inhalt hat.
            Assert.Contains(nameof(DashboardNeuerscheinungenViewModel.NewEpisodeGroupsVisibility), changed);
            Assert.Contains(nameof(DashboardNeuerscheinungenViewModel.NewReleasesSectionVisibility), changed);
            Assert.Contains(nameof(DashboardNeuerscheinungenViewModel.NewReleasesLoadingVisibility), changed);
        }

        [Fact]
        public void IsLoadingNewReleases_WhenSwitchedOff_AnnouncesBothVisibilities()
        {
            DashboardNeuerscheinungenViewModel sut = new()
            {
                IsLoadingNewReleases = true,
            };
            List<string?> changed = [];
            ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            sut.IsLoadingNewReleases = false;

            Assert.Contains(nameof(DashboardNeuerscheinungenViewModel.NewReleasesLoadingVisibility), changed);
            Assert.Contains(nameof(DashboardNeuerscheinungenViewModel.NewReleasesSectionVisibility), changed);
        }

        [Fact]
        public void SetGroups_WithEmptyList_HidesTheSectionAgain()
        {
            DashboardNeuerscheinungenViewModel sut = new();
            sut.SetGroups([BuildGroup("Januar 2026")]);

            sut.SetGroups([]);

            Assert.Equal(Visibility.Collapsed, sut.NewReleasesSectionVisibility);
        }

        private static NewEpisodesGroupViewModel BuildGroup(string header)
            => new(header, sortKey: 0, []);
    }
}
