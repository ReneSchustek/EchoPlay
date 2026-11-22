using EchoPlay.Core;
using System.Globalization;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Replication;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>Eine ausgepackte Mediathek, deren Datenpaket auf die Bestätigung wartet.</summary>
    /// <param name="Staged">Das vorbereitete Datenpaket samt Trockenlauf.</param>
    /// <param name="LibraryRoot">Der Ordner, in dem die Mediathek jetzt liegt.</param>
    /// <param name="Files">Zahl der ausgepackten Dateien.</param>
    public sealed record UnpackedLibrary(StagedPackage Staged, string LibraryRoot, int Files);

    /// <summary>
    /// Packt die lokale Mediathek samt Datenpaket für einen Umzug und packt sie auf dem Zielrechner
    /// wieder aus.
    /// </summary>
    public interface ILibraryTransferService
    {
        /// <summary>
        /// Packt die Mediathek mit einem frischen Datenpaket in ein Archiv.
        /// </summary>
        /// <param name="folder">Zielordner des Archivs.</param>
        /// <param name="credentialsPassword">Kennwort für die Zugangsdaten, oder <see langword="null"/> für ein Archiv ohne sie.</param>
        /// <param name="progress">Fortschritt von 0 bis 1.</param>
        /// <param name="cancellationToken">Bricht ab; es bleibt keine halbe Datei.</param>
        /// <returns>Pfad und Größe des Archivs, oder <see langword="null"/>, wenn keine lokale Mediathek eingerichtet ist.</returns>
        Task<(string Path, long Length)?> PackAsync(string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken);

        /// <summary>
        /// Packt ein Archiv in einen leeren Ordner aus und bereitet sein Datenpaket zum Einspielen vor.
        /// Scheitert das Vorbereiten, wird auch die ausgepackte Mediathek wieder entfernt.
        /// </summary>
        /// <param name="archivePath">Das Mediathek-Archiv.</param>
        /// <param name="targetRoot">Der künftige Stammpfad; leer oder noch nicht vorhanden.</param>
        /// <param name="progress">Fortschritt von 0 bis 1.</param>
        /// <param name="cancellationToken">Bricht ab; Ausgepacktes wird entfernt.</param>
        /// <returns>Die ausgepackte Mediathek mit dem vorbereiteten Paket.</returns>
        Task<UnpackedLibrary> UnpackAsync(string archivePath, string targetRoot, IProgress<double>? progress, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Standard-Umsetzung von <see cref="ILibraryTransferService"/>. Das Datenpaket entsteht über
    /// <see cref="IDataTransferService"/>; so gilt für Zugangsdaten und Prüfung genau dasselbe wie
    /// beim Export allein.
    /// </summary>
    /// <param name="scopeFactory">Für den Stammpfad aus den Einstellungen.</param>
    /// <param name="dataTransfer">Schreibt und prüft das Datenpaket.</param>
    /// <param name="databaseFile">Ort der Datenbank; daneben entsteht das ausgepackte Datenpaket.</param>
    /// <param name="timeProvider">Zeitquelle für Dateiname und Manifest; ohne Angabe die Systemuhr.</param>
    internal sealed class LibraryTransferService(
        IServiceScopeFactory scopeFactory,
        IDataTransferService dataTransfer,
        DatabaseFileLocation databaseFile,
        TimeProvider? timeProvider = null) : ILibraryTransferService
    {
        /// <summary>Name des ausgepackten Datenpakets im Datenverzeichnis; nach dem Vorbereiten gelöscht.</summary>
        internal const string UnpackedDataPackageName = "import.data" + PackageFormat.FileExtension;

        /// <summary>Anteil des Datenpakets am Fortschritt beim Packen; den Rest tragen die Dateien.</summary>
        private const double DataPackageShare = 0.1;

        private TimeProvider Clock => timeProvider ?? TimeProvider.System;

        /// <inheritdoc/>
        public async Task<(string Path, long Length)?> PackAsync(
            string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folder);

            string? root = await ReadLibraryRootAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            // Das Datenpaket entsteht in einem eigenen Unterordner: Unter seinem gewöhnlichen Namen
            // im gewählten Ordner überschriebe und löschte es einen Export derselben Minute.
            string scratch = Path.Combine(folder, $".echoplay-pack-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(scratch);
            try
            {
                (string dataPackage, _) = await dataTransfer.ExportAsync(
                    scratch, credentialsPassword, Scale(progress, 0, DataPackageShare), cancellationToken).ConfigureAwait(false);

                // Ortszeit, weil der Nutzer den Namen liest und ihn seinem Umzug zuordnen will.
                string fileName = "EchoPlay-Mediathek-" + Clock.GetLocalNow().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + LibraryArchiveFormat.FileExtension;
                string target = Path.Combine(folder, fileName);
                _ = await LibraryArchiveWriter.WriteAsync(
                    root, dataPackage, target, Clock, Scale(progress, DataPackageShare, 1), cancellationToken).ConfigureAwait(false);
                return (target, new FileInfo(target).Length);
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        /// <inheritdoc/>
        public async Task<UnpackedLibrary> UnpackAsync(
            string archivePath, string targetRoot, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            dataTransfer.DiscardImport();
            string dataPackage = Path.Combine(ParentFolder.Of(databaseFile.FilePath), UnpackedDataPackageName);

            LibraryExtraction extraction = await LibraryArchiveExtractor.ExtractAsync(
                archivePath, targetRoot, dataPackage, progress, cancellationToken).ConfigureAwait(false);
            try
            {
                StagedPackage staged = await dataTransfer.StageAsync(dataPackage, cancellationToken).ConfigureAwait(false);
                return new UnpackedLibrary(staged, extraction.TargetRoot, extraction.Manifest.Files.Count);
            }
            catch
            {
                // Ein Datenpaket, das sich nicht einspielen lässt, macht die ausgepackten Dateien
                // wertlos: Ohne Bestand kennt EchoPlay keine Zuordnung. Der Ordner war leer und wird
                // es wieder.
                LibraryArchiveExtractor.RemoveExtracted(extraction.TargetRoot, extraction.CreatedTargetRoot);
                throw;
            }
            finally
            {
                File.Delete(dataPackage);
            }
        }

        /// <summary>Rechnet einen Teilfortschritt auf einen Abschnitt des Gesamtfortschritts um.</summary>
        internal static IProgress<double>? Scale(IProgress<double>? progress, double from, double to)
            => progress is null ? null : new SynchronousProgress(value => progress.Report(from + ((to - from) * value)));

        private async Task<string?> ReadLibraryRootAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            AppSettings settings = await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>()
                .GetAsync(cancellationToken).ConfigureAwait(false);
            return settings.LocalLibraryEnabled ? settings.LocalLibraryRootPath : null;
        }

        /// <summary>
        /// Gibt jeden Wert sofort weiter. <see cref="Progress{T}"/> stellte ihn über den
        /// Synchronisationskontext zu, und die Anteile kämen in falscher Reihenfolge an.
        /// </summary>
        private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
        {
            public void Report(double value) => report(value);
        }
    }
}
