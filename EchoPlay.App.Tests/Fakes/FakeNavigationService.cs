using EchoPlay.App.Services;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="INavigationService"/>.
    /// Zeichnet Navigations- und Rückwärtssprünge auf, ohne einen WinUI-Frame zu benötigen.
    /// </summary>
    internal sealed class FakeNavigationService : INavigationService
    {
        /// <summary>Anzahl der <see cref="GoBack"/>-Aufrufe.</summary>
        public int GoBackCallCount { get; private set; }

        /// <summary>Steuert den Rückgabewert von <see cref="CanGoBack"/>.</summary>
        public bool CanGoBackResult { get; set; } = true;

        /// <summary>Alle aufgezeichneten Navigationen als (Target, Parameter)-Liste.</summary>
        public List<(NavigationTarget Target, object? Parameter)> Navigations { get; } = [];

        /// <inheritdoc/>
        public bool CanGoBack => CanGoBackResult;

        /// <inheritdoc/>
        public void NavigateTo(NavigationTarget target, object? parameter = null)
        {
            Navigations.Add((target, parameter));
        }

        /// <inheritdoc/>
        public bool GoBack()
        {
            GoBackCallCount++;
            return CanGoBackResult;
        }
    }
}
