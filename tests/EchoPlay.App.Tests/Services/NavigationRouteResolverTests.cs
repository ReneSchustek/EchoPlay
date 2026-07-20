using EchoPlay.App.Services;
using EchoPlay.App.Views;
using System;
using System.Collections.Generic;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den extrahierten Routing-Resolver.
    /// </summary>
    public sealed class NavigationRouteResolverTests
    {
        [Fact]
        public void Resolve_KnownTarget_ReturnsExpectedPageType()
        {
            Type pageType = NavigationRouteResolver.Resolve(NavigationTarget.Dashboard);

            Assert.Equal(typeof(DashboardPage), pageType);
        }

        [Fact]
        public void Resolve_AllEnumValues_AreRegistered()
        {
            // Waechter: jedes Enum-Mitglied muss ein Mapping haben, damit Hinzufuegen
            // einer neuen Seite ohne Routing-Eintrag den Test sofort kippt.
            foreach (NavigationTarget target in Enum.GetValues<NavigationTarget>())
            {
                Assert.True(NavigationRouteResolver.IsRegistered(target),
                    $"NavigationTarget.{target} ist nicht im Routing-Resolver registriert.");
            }
        }

        [Fact]
        public void RegisteredTargetCount_MatchesEnumValueCount()
        {
            int enumCount = Enum.GetValues<NavigationTarget>().Length;
            Assert.Equal(enumCount, NavigationRouteResolver.RegisteredTargetCount);
        }
    }
}
