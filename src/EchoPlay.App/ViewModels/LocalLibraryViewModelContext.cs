using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Services;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Service-Abhängigkeiten für <see cref="LocalLibraryViewModel"/>.
    /// Sub-VMs werden intern erzeugt und sind nicht Teil des Context-Records.
    /// </summary>
    internal sealed record LocalLibraryViewModelContext
    {
        /// <summary>Für die Datenbankzugriffe.</summary>
        public required IServiceScopeFactory ScopeFactory { get; init; }

        /// <summary>Liest die lokale Mediathek ein.</summary>
        public required ISyncService SyncService { get; init; }

        /// <summary>Spielt Spuren und Folgen ab.</summary>
        public required IPlayerService PlayerService { get; init; }

        /// <summary>Zeigt Fehler an.</summary>
        public required IErrorDialogService ErrorDialogService { get; init; }

        /// <summary>Fragt vor dem Entfernen, Löschen und Einlesen nach.</summary>
        public required IConfirmationDialogService ConfirmationDialogService { get; init; }

        /// <summary>Zieht die Zähler der Statusleiste nach.</summary>
        public required StatusBarViewModel StatusBar { get; init; }

        /// <summary>Lädt die Cover aus den Ordnern der Folgen.</summary>
        public required ILocalCoverLoader CoverLoader { get; init; }

        /// <summary>Meldet Beginn und Ende eines Einlesens an andere Seiten.</summary>
        public required IScanEventService ScanEventService { get; init; }

        /// <summary>Fragt im Offline-Modus nach dem Zugang ins Netz.</summary>
        public required IOnlineAccessGuard OnlineAccessGuard { get; init; }

        /// <summary>Zeitquelle für den Abschluss einer Folge.</summary>
        public required IClock Clock { get; init; }

        /// <summary>Liest und speichert Cover in der Datenbank; ohne ihn bleiben Platzhalter.</summary>
        public EchoPlay.App.Services.ICoverService? CoverService { get; init; }

        /// <summary>Schaltet die Überwachung einer Serie um.</summary>
        public IWatchToggleService? WatchToggleService { get; init; }

        /// <summary>Prüft beim Betreten, ob die Seite im aktuellen Modus erlaubt ist.</summary>
        public IPageModeGuard? PageModeGuard { get; init; }

        /// <summary>Plant und führt den Umbau der Ordnerstruktur aus.</summary>
        public IFolderRestructureCoordinator? RestructureCoordinator { get; init; }

        /// <summary>Ermittelt fehlende Folgen.</summary>
        public IMissingEpisodesCoordinator? MissingEpisodesCoordinator { get; init; }

        /// <summary>Sucht und übernimmt Cover.</summary>
        public IEpisodeCoverCoordinator? CoverCoordinator { get; init; }

        /// <summary>Protokolliert die Abläufe der Folgenliste.</summary>
        public ILogger? Logger { get; init; }

        /// <summary>Merkt sich die Filter der Serienliste über einen Seitenwechsel hinweg.</summary>
        public FilterStateStore? FilterState { get; init; }

        /// <summary>Benennt die Folgenordner nach dem Muster aus den Einstellungen um.</summary>
        public IEpisodeFolderRenameCoordinator? FolderRenameCoordinator { get; init; }

        /// <summary>Berichtigt die Angaben einer lokalen Folge oder Serie.</summary>
        public ILocalMetadataEditor? MetadataEditor { get; init; }
    }
}
