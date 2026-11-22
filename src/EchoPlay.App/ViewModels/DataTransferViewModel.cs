using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Replication;
using EchoPlay.Logger.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Export und Einspielen des Datenbestands in den Einstellungen unter Wartung.
    /// </summary>
    /// <remarks>
    /// Die Dialoge baut die Seite; dieses Ansichtsmodell hält Zustand, Fortschritt und Abbruch und
    /// übersetzt die erwartbaren Fehler in einen Satz für den Nutzer. Gefangen werden nur Fehler, die
    /// ein Umzug wirklich hervorbringt: abgelehntes Paket, zu wenig Platz, Datei- und
    /// Datenbankfehler, Abbruch. Alles andere ist ein Programmfehler und soll als solcher auffallen.
    /// </remarks>
    public sealed class DataTransferViewModel : ObservableObject
    {
        private readonly IDataTransferService _transfer;
        private readonly ILibraryTransferService _library;
        private readonly ISpotifyCredentialStore _credentials;
        private readonly IProcessLauncher _launcher;
        private readonly IAppInstanceRegistration _registration;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cancellation;
        private bool _isBusy;
        private double _progress;
        private string _statusText = string.Empty;

        /// <summary>
        /// Erzeugt das Ansichtsmodell.
        /// </summary>
        /// <param name="transfer">Export und Einspielen.</param>
        /// <param name="library">Packen und Auspacken der Mediathek.</param>
        /// <param name="credentials">Ob Zugangsdaten zum Mitnehmen da sind.</param>
        /// <param name="launcher">Startet die Anwendung nach dem Vormerken neu.</param>
        /// <param name="registration">Der Einzelinstanz-Anspruch für den Neustart.</param>
        /// <param name="loggerFactory">Die Logger-Fabrik.</param>
        internal DataTransferViewModel(
            IDataTransferService transfer,
            ILibraryTransferService library,
            ISpotifyCredentialStore credentials,
            IProcessLauncher launcher,
            IAppInstanceRegistration registration,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _logger = loggerFactory.CreateLogger("DataTransfer");
        }

        /// <summary>Ob gerade exportiert oder geprüft wird.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsNotBusy));
                    OnPropertyChanged(nameof(BusyVisibility));
                }
            }
        }

        /// <summary>Gegenteil von <see cref="IsBusy"/>, für die Schaltflächen.</summary>
        public bool IsNotBusy => !_isBusy;

        /// <summary>Sichtbarkeit von Fortschritt und Abbruch.</summary>
        public Visibility BusyVisibility => _isBusy ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Fortschritt des Exports von 0 bis 100.</summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        /// <summary>Ergebnis des letzten Vorgangs als Satz für den Nutzer.</summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (SetProperty(ref _statusText, value))
                {
                    OnPropertyChanged(nameof(StatusVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit der Ergebnismeldung.</summary>
        public Visibility StatusVisibility => string.IsNullOrEmpty(_statusText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Ob Zugangsdaten hinterlegt sind, die mitgenommen werden könnten.</summary>
        /// <returns><see langword="true"/>, wenn Spotify-Zugangsdaten da sind.</returns>
        public async Task<bool> HasCredentialsAsync() => await _credentials.GetAsync() is not null;

        /// <summary>Bricht den laufenden Vorgang ab.</summary>
        public void Cancel() => _cancellation?.Cancel();

        /// <summary>
        /// Schreibt ein Paket in den gewählten Ordner.
        /// </summary>
        /// <param name="folder">Der Zielordner.</param>
        /// <param name="credentialsPassword">Kennwort für die Zugangsdaten, oder <see langword="null"/>.</param>
        /// <returns>Der Task ist abgeschlossen, wenn das Paket steht oder der Fehler gemeldet ist.</returns>
        public async Task ExportAsync(string folder, string? credentialsPassword)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("DataExport");
            using CancellationTokenSource cancellation = Begin();
            Progress<double> progress = new(value => Progress = value * 100);
            try
            {
                (string path, long length) = await _transfer.ExportAsync(folder, credentialsPassword, progress, cancellation.Token);
                StatusText = Format("DataExportDone", "Exportiert: {0} ({1}).", Path.GetFileName(path), FormatSize(length));
                _logger.Info("Datenbestand exportiert: {File}, {Length} Byte.", Path.GetFileName(path), length);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                StatusText = Describe("DataExportFailed", "Export fehlgeschlagen: {0}", ex);
                _logger.Warning("Export fehlgeschlagen: {Reason}", ex.Message);
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        /// Prüft ein Paket und bereitet es vor.
        /// </summary>
        /// <param name="packagePath">Das Paket.</param>
        /// <returns>Das vorbereitete Paket, oder <see langword="null"/>, wenn es abgelehnt wurde.</returns>
        public async Task<StagedPackage?> StageAsync(string packagePath)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("DataImportCheck");
            using CancellationTokenSource cancellation = Begin();
            try
            {
                StatusText = string.Empty;
                return await _transfer.StageAsync(packagePath, cancellation.Token);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                StatusText = Describe("DataImportRejected", "Paket nicht eingespielt: {0}", ex);
                _logger.Warning("Paket abgelehnt: {Reason}", ex.Message);
                return null;
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        /// Merkt das Paket vor und startet neu.
        /// </summary>
        /// <param name="staged">Das vorbereitete Paket.</param>
        /// <param name="credentialsPassword">Kennwort für die Zugangsdaten im Paket, oder <see langword="null"/>.</param>
        /// <param name="libraryRoot">Neuer Stammpfad nach dem Auspacken einer Mediathek, sonst <see langword="null"/>.</param>
        /// <returns><see langword="false"/>, wenn das Kennwort nicht passt.</returns>
        public bool ScheduleAndRestart(StagedPackage staged, string? credentialsPassword, string? libraryRoot = null)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("DataImport");
            if (!_transfer.ScheduleImport(staged, credentialsPassword, libraryRoot))
            {
                StatusText = SafeResourceLoader.Get("DataImportWrongPassword", "Das Kennwort passt nicht zu den Zugangsdaten im Paket.");
                return false;
            }

            _logger.Info("Einspielen vorgemerkt, Neustart folgt.");
            if (!AppRestart.TryRestart(_launcher, _registration, _logger, "Einspielen eines Pakets"))
            {
                StatusText = SafeResourceLoader.Get("DataImportRestartManually", "Eingespielt wird beim nächsten Start. Bitte EchoPlay jetzt beenden und neu starten.");
            }

            return true;
        }

        /// <summary>
        /// Packt die Mediathek samt Datenpaket in ein Archiv im gewählten Ordner.
        /// </summary>
        /// <param name="folder">Zielordner.</param>
        /// <param name="credentialsPassword">Kennwort für die Zugangsdaten, oder <see langword="null"/> ohne sie.</param>
        /// <returns>Der Task ist abgeschlossen, wenn das Archiv steht oder der Fehler gemeldet ist.</returns>
        public async Task PackLibraryAsync(string folder, string? credentialsPassword)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("LibraryPack");
            using CancellationTokenSource cancellation = Begin();
            Progress<double> progress = new(value => Progress = value * 100);
            try
            {
                (string Path, long Length)? packed = await _library.PackAsync(folder, credentialsPassword, progress, cancellation.Token);
                if (packed is not { } archive)
                {
                    StatusText = SafeResourceLoader.Get("LibraryPackNoRoot", "Es ist keine lokale Mediathek eingerichtet; zum Packen fehlt der Stammpfad.");
                    return;
                }

                StatusText = Format("LibraryPackDone", "Gepackt: {0} ({1}).", Path.GetFileName(archive.Path), FormatSize(archive.Length));
                _logger.Info("Mediathek gepackt: {File}, {Length} Byte.", Path.GetFileName(archive.Path), archive.Length);
            }
            catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException)
            {
                StatusText = Describe("LibraryPackFailed", "Packen fehlgeschlagen: {0}", ex);
                _logger.Warning("Packen der Mediathek fehlgeschlagen: {Reason}", ex.Message);
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        /// Packt ein Mediathek-Archiv in einen leeren Ordner aus und bereitet sein Datenpaket vor.
        /// </summary>
        /// <param name="archivePath">Das Archiv.</param>
        /// <param name="targetRoot">Der künftige Stammpfad.</param>
        /// <returns>Die ausgepackte Mediathek, oder <see langword="null"/>, wenn es scheiterte; dann steht der Grund in <see cref="StatusText"/>.</returns>
        public async Task<UnpackedLibrary?> UnpackLibraryAsync(string archivePath, string targetRoot)
        {
            using IDisposable userAction = UserActionScope.BeginUserAction("LibraryUnpack");
            using CancellationTokenSource cancellation = Begin();
            Progress<double> progress = new(value => Progress = value * 100);
            try
            {
                StatusText = string.Empty;
                UnpackedLibrary unpacked = await _library.UnpackAsync(archivePath, targetRoot, progress, cancellation.Token);
                _logger.Info("Mediathek ausgepackt: {Files} Dateien nach {Root}.", unpacked.Files, unpacked.LibraryRoot);
                return unpacked;
            }
            catch (Exception ex) when (IsExpected(ex) || ex is ArgumentException)
            {
                StatusText = Describe("LibraryUnpackFailed", "Auspacken fehlgeschlagen: {0}", ex);
                _logger.Warning("Auspacken der Mediathek fehlgeschlagen: {Reason}", ex.Message);
                return null;
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        /// Verwirft das Datenpaket einer ausgepackten Mediathek, wenn der Nutzer nicht bestätigt. Die
        /// Dateien bleiben liegen; wer nur sie wollte, hat sie damit.
        /// </summary>
        /// <param name="unpacked">Die ausgepackte Mediathek.</param>
        public void DiscardUnpacked(UnpackedLibrary unpacked)
        {
            ArgumentNullException.ThrowIfNull(unpacked);
            _transfer.DiscardImport();
            StatusText = Format("LibraryUnpackKept", "Eingespielt wurde nichts; die Dateien liegen in {0}.", unpacked.LibraryRoot);
        }

        /// <summary>Verwirft ein vorbereitetes Paket, wenn der Nutzer nicht bestätigt.</summary>
        public void Discard()
        {
            _transfer.DiscardImport();
            StatusText = SafeResourceLoader.Get("DataImportCancelled", "Einspielen abgebrochen; der Bestand ist unverändert.");
        }

        private static bool IsExpected(Exception ex) => ex is PackageRejectedException
            or InsufficientSpaceException
            or OperationCanceledException
            or IOException
            or UnauthorizedAccessException
            or SqliteException
            or InvalidDataException
            or CryptographicException;

        private static string Describe(string key, string fallback, Exception ex) => ex switch
        {
            OperationCanceledException => SafeResourceLoader.Get("DataTransferCancelled", "Abgebrochen."),
            InsufficientSpaceException space => Format(
                "DataTransferNoSpace", "Zu wenig Platz: gebraucht {0}, frei {1}.", FormatSize(space.RequiredBytes), FormatSize(space.AvailableBytes)),
            _ => Format(key, fallback, ex.Message),
        };

        private static string Format(string key, string fallback, params object[] values) =>
            string.Format(CultureInfo.CurrentCulture, SafeResourceLoader.Get(key, fallback), values);

        private static string FormatSize(long bytes) =>
            bytes >= 1024L * 1024 * 1024
                ? (bytes / (1024d * 1024 * 1024)).ToString("0.00", CultureInfo.CurrentCulture) + " GB"
                : (bytes / (1024d * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

        private CancellationTokenSource Begin()
        {
            CancellationTokenSource cancellation = new();
            _cancellation = cancellation;
            Progress = 0;
            IsBusy = true;
            return cancellation;
        }

        private void End()
        {
            _cancellation = null;
            IsBusy = false;
        }
    }
}
