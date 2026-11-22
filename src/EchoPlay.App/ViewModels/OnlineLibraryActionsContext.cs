using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Dienste der Online-Mediathek für
    /// <see cref="OnlineLibraryViewModel"/> und <see cref="OnlineLibraryActions"/>. Pflicht sind die
    /// Dienste, ohne die die Seite nicht arbeitet; die übrigen schalten Zusatzfunktionen zu.
    /// </summary>
    public sealed record OnlineLibraryActionsContext
    {
        /// <summary>Für die Datenbankzugriffe.</summary>
        public required IServiceScopeFactory ScopeFactory { get; init; }

        /// <summary>Fragt vor dem Entfernen und Abonnieren nach.</summary>
        public required IConfirmationDialogService ConfirmationDialogService { get; init; }

        /// <summary>Sucht beim Anbieter und importiert.</summary>
        public required ImportService ImportService { get; init; }

        /// <summary>Zeigt Fehler an.</summary>
        public required IErrorDialogService ErrorDialogService { get; init; }

        /// <summary>Liefert die Anzeigetexte.</summary>
        public required ILocalizationService LocalizationService { get; init; }

        /// <summary>Fragt im Offline-Modus nach dem Zugang ins Netz.</summary>
        public required IOnlineAccessGuard OnlineAccessGuard { get; init; }

        /// <summary>Lädt fehlende Folgen-Cover nach; ohne ihn entfällt das.</summary>
        public EpisodeCoverCacheService? CoverCacheService { get; init; }

        /// <summary>Liest und speichert Cover in der Datenbank; ohne ihn bleiben Platzhalter.</summary>
        public ICoverService? CoverService { get; init; }

        /// <summary>Stellt lokale Cover der Serie bereit.</summary>
        public BackgroundCoverService? BackgroundCoverService { get; init; }

        /// <summary>Schaltet die Überwachung einer Serie um.</summary>
        public IWatchToggleService? WatchToggleService { get; init; }

        /// <summary>Lädt Cover vom Anbieter herunter.</summary>
        public required ICoverDownloader CoverDownloader { get; init; }

        /// <summary>Drosselt die Abfragen beim Anbieter.</summary>
        public IHostRateLimiter? RateLimiter { get; init; }
    }
}
