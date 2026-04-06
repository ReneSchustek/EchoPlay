using System;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Datenmodell für eine Zeile in der Episodenliste der Detailansicht.
    /// </summary>
    public sealed class EpisodeRowViewModel
    {
        /// <summary>Datenbank-ID der Episode.</summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Formatierte Episodennummer, z.B. "42" oder "–" wenn nicht bekannt.
        /// </summary>
        public string NumberText { get; init; } = string.Empty;

        /// <summary>Titel der Episode.</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Formatierte Dauer, z.B. "1:23:45".
        /// Leer wenn keine Dauer bekannt.
        /// </summary>
        public string DurationText { get; init; } = string.Empty;

        /// <summary>Wiedergabestatus als Segoe-Fluent-Icon-Glyph.</summary>
        public string StatusGlyph { get; init; } = string.Empty;

        /// <summary>Zugängliche Beschreibung des Status für Screen Reader.</summary>
        public string StatusLabel { get; init; } = string.Empty;

        /// <summary>
        /// Startet die Wiedergabe dieser Episode.
        /// Wird von <see cref="SeriesDetailViewModel"/> mit der passenden Aktion belegt.
        /// </summary>
        public ICommand PlayCommand { get; init; } = new RelayCommand(() => { });
    }
}
