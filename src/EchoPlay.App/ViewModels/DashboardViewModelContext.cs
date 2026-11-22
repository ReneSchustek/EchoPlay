using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Dienste für <see cref="DashboardViewModel"/>.
    /// Dieselbe Form wie <see cref="LocalLibraryViewModelContext"/>: Zehn einzelne
    /// Konstruktor-Parameter wären an der Aufrufstelle nicht auseinanderzuhalten,
    /// und jeder neue Dienst verschöbe die Reihenfolge erneut.
    /// Die Abschnitts-Ansichtsmodelle entstehen intern und gehören nicht hierher.
    /// </summary>
    internal sealed record DashboardViewModelContext
    {
        /// <summary>Für datenbanknahe Zugriffe beim Laden und in den Kachel-Befehlen.</summary>
        public required IServiceScopeFactory ScopeFactory { get; init; }

        /// <summary>Für Hinweise bei nicht verfügbaren Folgen.</summary>
        public required IErrorDialogService ErrorDialogService { get; init; }

        /// <summary>Für Rückfragen vor Änderungen am Hörstatus.</summary>
        public required IConfirmationDialogService ConfirmationDialogService { get; init; }

        /// <summary>Startet die Wiedergabe.</summary>
        public required IPlayerService PlayerService { get; init; }

        /// <summary>Fabrik für den Protokollkanal.</summary>
        public required ILoggerFactory LoggerFactory { get; init; }

        /// <summary>Zentraler Cover-Dienst. Nullable für Tests.</summary>
        public ICoverService? CoverService { get; init; }

        /// <summary>Liefert die Anzeigetexte. Nullable für Tests.</summary>
        public ILocalizationService? LocalizationService { get; init; }

        /// <summary>Zeitquelle. Nullable; fehlt sie, gilt die Systemzeit.</summary>
        public IClock? Clock { get; init; }

        /// <summary>Trägt fehlende Folgen-Cover nach. Nullable für Tests.</summary>
        public BackgroundCoverService? BackgroundCoverService { get; init; }

        /// <summary>Meldet Änderungen am Neuerscheinungs-Zwischenspeicher. Nullable für Tests.</summary>
        public INewReleaseEventService? NewReleaseEventService { get; init; }

        /// <summary>Baut den Veröffentlichungskalender auf und meldet neue Einträge. Nullable für Tests.</summary>
        public ReleaseCalendarRefreshService? ReleaseCalendarService { get; init; }
    }
}
