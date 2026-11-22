using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Core.Replication.Exchange
{
    /// <summary>
    /// Die Arten von Daten, die zwischen Geräten abgeglichen werden. Der Name steht so in jeder
    /// Protokolldatei und darf sich deshalb nicht ändern.
    /// </summary>
    /// <remarks>
    /// Nicht dabei sind Cover und alles Gerätegebundene (Pfade, Lautstärke, Zugangsdaten): Cover sind
    /// ein Zwischenspeicher und lassen sich neu laden, Gerätegebundenes gilt nur auf einem Rechner.
    /// </remarks>
    public static class ChangeKinds
    {
        /// <summary>Eine Serie; Schlüssel nach <see cref="BusinessKey.ForSeries"/>.</summary>
        public const string Series = "Series";

        /// <summary>Eine Folge; Schlüssel nach <see cref="BusinessKey.ForEpisode"/>, übergeordnet die Serie.</summary>
        public const string Episode = "Episode";

        /// <summary>Der Hörstand einer Folge; Schlüssel und übergeordnet ist die Folge.</summary>
        public const string PlaybackState = "PlaybackState";

        /// <summary>Ein gemerkter überwachter Titel; Schlüssel ist der normalisierte Titel.</summary>
        public const string WatchedTitle = "WatchedTitle";

        /// <summary>Ein dauerhaft ausgeblendeter Hinweis; Schlüssel ist sein Name.</summary>
        public const string DialogSuppression = "DialogSuppression";

        /// <summary>Die gemeinsamen Einstellungen; es gibt genau einen Satz mit dem Schlüssel <see cref="SettingsKey"/>.</summary>
        public const string Settings = "Settings";

        /// <summary>Die Reihenfolge eines Abschnitts der Startseite als ganze Liste; Schlüssel ist der Abschnitt.</summary>
        public const string DashboardOrder = "DashboardOrder";

        /// <summary>Der einzige Schlüssel der Art <see cref="Settings"/>.</summary>
        public const string SettingsKey = "settings";

        /// <summary>Das Feld der Art <see cref="DashboardOrder"/>: die Serien-Schlüssel in Reihenfolge, je Zeile einer.</summary>
        public const string OrderField = "Order";

        /// <summary>Alle Arten, in der Reihenfolge, in der sie übernommen werden: Übergeordnetes zuerst.</summary>
        public static IReadOnlyList<string> All { get; } =
            [Series, Episode, PlaybackState, WatchedTitle, DialogSuppression, Settings, DashboardOrder];

        /// <summary>Ob <paramref name="kind"/> eine bekannte Art ist.</summary>
        /// <param name="kind">Die Art aus einer Datei.</param>
        /// <returns><see langword="true"/>, wenn sie bekannt ist.</returns>
        public static bool IsKnown([NotNullWhen(true)] string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
    }
}
