using EchoPlay.App.Services;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IThemeService"/>.
    /// Zeichnet <see cref="ApplyTheme"/>-Aufrufe auf, ohne WinUI-3-Ressourcen zu verändern.
    /// </summary>
    internal sealed class FakeThemeService : IThemeService
    {
        /// <summary>Alle aufgerufenen Theme-Namen in Reihenfolge.</summary>
        public List<string> AppliedThemes { get; } = [];

        /// <inheritdoc/>
        public void ApplyTheme(string themeName)
        {
            AppliedThemes.Add(themeName);
        }
    }
}
