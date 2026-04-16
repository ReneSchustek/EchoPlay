using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für den Event-Subscription-Kontrakt von <see cref="DashboardFavoritenViewModel"/>.
    /// Prüft, dass wiederholtes <c>SetItems</c> keine Handler akkumuliert und dass <c>Dispose</c>
    /// alle Card- und Collection-Subscriptions sauber löst.
    /// </summary>
    public sealed class DashboardFavoritenViewModelTests
    {
        [Fact]
        public void SetItems_CalledTwiceWithSameCards_DoesNotAccumulateHandlers()
        {
            DashboardFavoritenViewModel vm = new(new NoopServiceScopeFactory(), new FakeLogger());
            FavoriteSeriesCardViewModel card = CreateCard();

            vm.SetItems([card]);
            vm.SetItems([card]);

            // Bei wiederholtem SetItems mit derselben Card-Instanz darf der
            // RemovedFromFavorites-Handler nur einmal angemeldet sein.
            Assert.Equal(1, SubscriberCount(card, nameof(FavoriteSeriesCardViewModel.RemovedFromFavorites)));
        }

        [Fact]
        public void Dispose_UnsubscribesAllCardHandlers()
        {
            DashboardFavoritenViewModel vm = new(new NoopServiceScopeFactory(), new FakeLogger());
            FavoriteSeriesCardViewModel card1 = CreateCard();
            FavoriteSeriesCardViewModel card2 = CreateCard();

            vm.SetItems([card1, card2]);
            vm.Dispose();

            Assert.Equal(0, SubscriberCount(card1, nameof(FavoriteSeriesCardViewModel.RemovedFromFavorites)));
            Assert.Equal(0, SubscriberCount(card2, nameof(FavoriteSeriesCardViewModel.RemovedFromFavorites)));
        }

        [Fact]
        public void SetItems_ReplacingCards_UnsubscribesOldCardsBeforeSubscribingNew()
        {
            DashboardFavoritenViewModel vm = new(new NoopServiceScopeFactory(), new FakeLogger());
            FavoriteSeriesCardViewModel oldCard = CreateCard();
            FavoriteSeriesCardViewModel newCard = CreateCard();

            vm.SetItems([oldCard]);
            vm.SetItems([newCard]);

            // Alte Karte darf keinen Handler mehr halten, neue Karte genau einen.
            Assert.Equal(0, SubscriberCount(oldCard, nameof(FavoriteSeriesCardViewModel.RemovedFromFavorites)));
            Assert.Equal(1, SubscriberCount(newCard, nameof(FavoriteSeriesCardViewModel.RemovedFromFavorites)));
        }

        private static FavoriteSeriesCardViewModel CreateCard()
        {
            return new FavoriteSeriesCardViewModel(
                seriesId: Guid.NewGuid(),
                seriesName: "Testserie",
                coverImage: null,
                scopeFactory: new NoopServiceScopeFactory(),
                confirmationDialogService: new FakeConfirmationDialogService());
        }

        /// <summary>
        /// Liest per Reflection das private Backing-Field eines field-like Events
        /// und zählt die Invocation-List-Einträge (0 bei keinem Abonnenten).
        /// </summary>
        private static int SubscriberCount(object source, string eventName)
        {
            FieldInfo? field = source.GetType().GetField(
                eventName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(source) is not MulticastDelegate del)
            {
                return 0;
            }

            return del.GetInvocationList().Length;
        }

        /// <summary>
        /// Dummy-ScopeFactory für Tests, die keinen DB-Pfad betreten —
        /// CreateScope wirft bei unbeabsichtigter Nutzung, damit der Test den
        /// fehlerhaften Pfad klar meldet statt in NullRef zu laufen.
        /// </summary>
        private sealed class NoopServiceScopeFactory : IServiceScopeFactory
        {
            public IServiceScope CreateScope()
                => throw new InvalidOperationException("ScopeFactory in diesem Test nicht erwartet.");
        }
    }
}
