using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Neuerscheinungs-Abschnitt der Übersicht: wann er erscheint und wann die
    /// Gruppen sichtbar werden. Ein Abschnitt, der ohne Inhalt stehen bleibt, ist Ballast.
    /// </summary>
    public sealed class DashboardNeuerscheinungenViewModelTests
    {
        [Fact]
        public void NewReleasesSection_WithoutGroups_StaysHidden()
        {
            DashboardNeuerscheinungenViewModel sut = new();

            Assert.Equal(Visibility.Collapsed, sut.NewReleasesSectionVisibility);
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
        public void SetGroups_AnnouncesEveryDependentVisibility()
        {
            DashboardNeuerscheinungenViewModel sut = new();
            List<string?> changed = [];
            ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            sut.SetGroups([BuildGroup("Februar 2026")]);

            // Ohne diese Meldungen bliebe der Abschnitt unsichtbar, obwohl er Inhalt hat.
            Assert.Contains(nameof(DashboardNeuerscheinungenViewModel.NewEpisodeGroupsVisibility), changed);
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
            => new(header, []);
    }
}
