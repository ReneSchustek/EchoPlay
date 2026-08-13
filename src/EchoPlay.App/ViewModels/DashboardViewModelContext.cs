using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Dienste für <see cref="DashboardViewModel"/>.
    /// Dieselbe Form wie <see cref="LocalLibraryViewModelContext"/>: Zehn einzelne
    /// Konstruktor-Parameter waren an der Aufrufstelle nicht mehr auseinanderzuhalten,
    /// und jeder neue Dienst hätte die Reihenfolge erneut verschoben.
    /// Die Abschnitts-Ansichtsmodelle entstehen intern und gehören nicht hierher.
    /// </summary>
    /// <param name="ScopeFactory">Für datenbanknahe Zugriffe beim Laden und in den Kachel-Befehlen.</param>
    /// <param name="ErrorDialogService">Für Hinweise bei nicht verfügbaren Folgen.</param>
    /// <param name="ConfirmationDialogService">Für Rückfragen vor Änderungen am Hörstatus.</param>
    /// <param name="PlayerService">Startet die Wiedergabe.</param>
    /// <param name="LoggerFactory">Fabrik für den Protokollkanal.</param>
    /// <param name="CoverService">Zentraler Cover-Dienst. Nullable für Tests.</param>
    /// <param name="LocalizationService">Liefert die Anzeigetexte. Nullable für Tests.</param>
    /// <param name="Clock">Zeitquelle. Nullable — dann gilt die Systemzeit.</param>
    /// <param name="BackgroundCoverService">Trägt fehlende Folgen-Cover nach. Nullable für Tests.</param>
    /// <param name="NewReleaseEventService">Meldet Änderungen am Neuerscheinungs-Zwischenspeicher. Nullable für Tests.</param>
    internal sealed record DashboardViewModelContext(
        IServiceScopeFactory ScopeFactory,
        IErrorDialogService ErrorDialogService,
        IConfirmationDialogService ConfirmationDialogService,
        IPlayerService PlayerService,
        ILoggerFactory LoggerFactory,
        ICoverService? CoverService = null,
        ILocalizationService? LocalizationService = null,
        IClock? Clock = null,
        BackgroundCoverService? BackgroundCoverService = null,
        INewReleaseEventService? NewReleaseEventService = null);
}
