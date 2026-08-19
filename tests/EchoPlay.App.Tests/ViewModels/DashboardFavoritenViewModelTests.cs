using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Favoriten-Abschnitt des Übersichtsbildschirms: Belegen der Kachelreihe,
    /// Speichern der Reihenfolge nach dem Verschieben und das Abmelden der Rückrufe.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge ist eine Nutzerentscheidung, die einen Neustart überleben muss —
    /// deshalb steht das Speichern hier unter Test und nicht nur das Anzeigen. Der zweite
    /// Schwerpunkt sind die Rückrufe: Sie werden an jeder Kachel angemeldet, und wenn sie
    /// beim Ersetzen der Reihe nicht abgemeldet werden, meldet jede spätere Änderung sich
    /// so oft, wie die Ansicht seit dem Start neu geladen wurde.
    /// </remarks>
    public sealed class DashboardFavoritenViewModelTests
    {
        [Fact]
        public void SetItems_FillsTheRowAndShowsTheSection()
        {
            Harness harness = Harness.Build();
            FavoriteSeriesCardViewModel card = harness.BuildCard("Die drei Fragezeichen");

            harness.ViewModel.SetItems([card]);

            _ = Assert.Single(harness.ViewModel.FavoriteSeries);
            Assert.Equal(Visibility.Visible, harness.ViewModel.FavoriteSectionVisibility);
        }

        [Fact]
        public void SetItems_WithoutEntries_HidesTheSection()
        {
            Harness harness = Harness.Build();

            harness.ViewModel.SetItems([]);

            // Eine leere Überschrift ohne Kacheln darunter sieht aus wie ein Ladefehler.
            Assert.Equal(Visibility.Collapsed, harness.ViewModel.FavoriteSectionVisibility);
        }

        [Fact]
        public void SetItems_WithoutList_Throws()
        {
            Harness harness = Harness.Build();

            _ = Assert.Throws<ArgumentNullException>(() => harness.ViewModel.SetItems(null!));
        }

        [Fact]
        public void SetItems_AnnouncesTheChange()
        {
            Harness harness = Harness.Build();
            int announced = 0;
            harness.ViewModel.FavoritesChanged += () => announced++;

            harness.ViewModel.SetItems([harness.BuildCard("Bibi Blocksberg")]);

            // Das übergeordnete Ansichtsmodell rechnet daraufhin den Hinweis „noch keine
            // Favoriten" neu aus. Bleibt die Meldung aus, steht er über einer vollen Reihe.
            Assert.Equal(1, announced);
        }

        [Fact]
        public async Task SetItems_TwiceWithTheSameCards_DoesNotStackTheHandlers()
        {
            Harness harness = Harness.Build();
            FavoriteSeriesCardViewModel card = harness.BuildCard("Benjamin Blümchen");

            harness.ViewModel.SetItems([card]);
            harness.ViewModel.SetItems([card]);

            harness.DragToFront(0);
            await Task.Yield();

            // Zweimal dieselbe Kachel zu setzen ist der Regelfall — die Ansicht lädt neu.
            // Häuften sich die Rückrufe dabei an, schriebe ein einziges Verschieben so oft
            // in die Ablage, wie die Ansicht seit dem Start geladen wurde.
            _ = Assert.Single(harness.ViewModel.FavoriteSeries);
            Assert.Equal(1, harness.Positions.SaveCallCount);
        }

        [Fact]
        public async Task Reordering_SavesTheNewOrder()
        {
            Harness harness = Harness.Build();
            FavoriteSeriesCardViewModel first = harness.BuildCard("Erste");
            FavoriteSeriesCardViewModel second = harness.BuildCard("Zweite");
            harness.ViewModel.SetItems([first, second]);

            harness.DragToFront(1);
            await Task.Yield();

            // Wer seine Favoriten sortiert, erwartet die Reihenfolge beim nächsten Start
            // wieder. Ohne das Speichern wäre die Arbeit mit dem Schließen des Fensters weg.
            Assert.Equal(1, harness.Positions.SaveCallCount);
            Assert.Equal([second.SeriesId, first.SeriesId], harness.Positions.LastOrder);
        }

        [Fact]
        public async Task SaveOrder_WritesEveryCardInTheDisplayedOrder()
        {
            Harness harness = Harness.Build();
            FavoriteSeriesCardViewModel first = harness.BuildCard("Erste");
            FavoriteSeriesCardViewModel second = harness.BuildCard("Zweite");
            FavoriteSeriesCardViewModel third = harness.BuildCard("Dritte");
            harness.ViewModel.SetItems([first, second, third]);

            await harness.ViewModel.SaveFavoriteSeriesOrderAsync();

            Assert.Equal([first.SeriesId, second.SeriesId, third.SeriesId], harness.Positions.LastOrder);
        }

        [Fact]
        public async Task SaveOrder_WhenTheStoreIsUnavailable_KeepsGoing()
        {
            Harness harness = Harness.Build(
                saveFailure: new InvalidOperationException("Ablage gesperrt"));
            harness.ViewModel.SetItems([harness.BuildCard("Die drei Fragezeichen")]);

            await harness.ViewModel.SaveFavoriteSeriesOrderAsync();

            // Eine gesperrte Ablage darf die Übersicht nicht mitreißen. Die Reihenfolge ist
            // dann nicht gespeichert — die Ansicht bleibt aber bedienbar.
            Assert.Equal(1, harness.Positions.SaveCallCount);
        }

        [Fact]
        public async Task Dispose_StopsSavingOnLaterChanges()
        {
            Harness harness = Harness.Build();
            harness.ViewModel.SetItems([harness.BuildCard("Erste"), harness.BuildCard("Zweite")]);

            harness.ViewModel.Dispose();
            harness.DragToFront(1);
            await Task.Yield();

            // Nach dem Verlassen der Seite hängt kein Rückruf mehr an der Sammlung. Bliebe
            // einer, schriebe eine verwaiste Ansicht weiter in die Ablage.
            Assert.Equal(0, harness.Positions.SaveCallCount);
        }

        /// <summary>
        /// Baut das Ansichtsmodell samt Umfeld und liefert die Fabrik für Kacheln.
        /// </summary>
        private sealed class Harness
        {
            public required DashboardFavoritenViewModel ViewModel { get; init; }

            public required FakeDashboardPositionDataService Positions { get; init; }

            public required IServiceScopeFactory ScopeFactory { get; init; }

            /// <summary>
            /// Zieht die Kachel an der Stelle <paramref name="index"/> an den Anfang, so wie
            /// es die Liste tut: Sie nimmt den Eintrag heraus und setzt ihn neu ein. Ein
            /// <c>Move</c> auf der Sammlung meldet eine andere Art von Änderung und käme
            /// beim Ansichtsmodell gar nicht an.
            /// </summary>
            public void DragToFront(int index)
            {
                FavoriteSeriesCardViewModel dragged = ViewModel.FavoriteSeries[index];
                _ = ViewModel.FavoriteSeries.Remove(dragged);
                ViewModel.FavoriteSeries.Insert(0, dragged);
            }

            /// <summary>Baut eine Kachel ohne Bild — das Bildobjekt entstünde nur am Fenster.</summary>
            public FavoriteSeriesCardViewModel BuildCard(string seriesName) =>
                new(Guid.NewGuid(), seriesName, coverImage: null, ScopeFactory,
                    new FakeConfirmationDialogService());

            public static Harness Build(Exception? saveFailure = null)
            {
                FakeDashboardPositionDataService positions = new(saveFailure);

                ServiceCollection services = new();
                _ = services.AddScoped<IDashboardPositionDataService>(_ => positions);
                _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());

                ServiceProvider provider = services.BuildServiceProvider();
                IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                return new Harness
                {
                    ViewModel = new DashboardFavoritenViewModel(scopeFactory, new FakeLogger()),
                    Positions = positions,
                    ScopeFactory = scopeFactory,
                };
            }
        }
    }
}
