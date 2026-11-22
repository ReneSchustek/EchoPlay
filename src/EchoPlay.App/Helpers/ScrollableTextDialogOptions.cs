using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Beschriftung, Höhe und Schrift des Textdialogs. Ohne Angabe zeigt der Dialog nur eine
    /// Schließen-Schaltfläche in normaler Schrift bei 400 Pixel Höhe.
    /// </summary>
    internal sealed record ScrollableTextDialogOptions
    {
        /// <summary>Optionaler Primärbutton, etwa "Als TXT speichern"; <see langword="null"/> zeigt nur die Schließen-Schaltfläche.</summary>
        public string? PrimaryButtonText { get; init; }

        /// <summary>Beschriftung der Schließen-Schaltfläche; ohne Angabe die Ressource <c>CommonCloseButton</c>.</summary>
        public string? CloseButtonText { get; init; }

        /// <summary>Maximale Höhe des ScrollViewers.</summary>
        public double MaxHeight { get; init; } = 400;

        /// <summary>Schaltet die Mono-Schrift des Themes ein (sinnvoll für Berichte und Dateilisten).</summary>
        public bool UseMonospace { get; init; }

        /// <summary>Schriftgröße bei Mono-Schrift; Berichte können kleiner laufen.</summary>
        public double MonospaceFontSize { get; init; } = 14;

        /// <summary>Welcher Button durch Enter ausgelöst wird; wichtig bei destruktiven Aktionen.</summary>
        public ContentDialogButton DefaultButton { get; init; } = ContentDialogButton.None;
    }
}
