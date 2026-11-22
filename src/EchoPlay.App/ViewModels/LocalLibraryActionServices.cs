using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Dienste, die nur die Aktionen der lokalen Mediathek brauchen. Pflicht ist, ohne das die
    /// Aktionen nicht arbeiten; die Koordinatoren schalten einzelne Funktionen zu.
    /// </summary>
    internal sealed record LocalLibraryActionServices
    {
        /// <summary>Für die Datenbankzugriffe.</summary>
        public required IServiceScopeFactory ScopeFactory { get; init; }

        /// <summary>Fragt vor dem Entfernen und Löschen nach.</summary>
        public required IConfirmationDialogService ConfirmationDialogService { get; init; }

        /// <summary>Zieht die Zähler der Statusleiste nach.</summary>
        public required StatusBarViewModel StatusBar { get; init; }

        /// <summary>Fragt im Offline-Modus nach dem Zugang ins Netz.</summary>
        public required IOnlineAccessGuard OnlineAccessGuard { get; init; }

        /// <summary>Zeitquelle für den Abschluss einer Folge.</summary>
        public required IClock Clock { get; init; }

        /// <summary>Schaltet die Überwachung einer Serie um.</summary>
        public IWatchToggleService? WatchToggleService { get; init; }

        /// <summary>Plant und führt den Umbau der Ordnerstruktur aus.</summary>
        public IFolderRestructureCoordinator? RestructureCoordinator { get; init; }

        /// <summary>Ermittelt fehlende Folgen.</summary>
        public IMissingEpisodesCoordinator? MissingEpisodesCoordinator { get; init; }

        /// <summary>Sucht und übernimmt Cover.</summary>
        public IEpisodeCoverCoordinator? CoverCoordinator { get; init; }
    }
}
