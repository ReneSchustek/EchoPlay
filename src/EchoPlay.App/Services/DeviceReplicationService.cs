using System.Data.Common;
using System.Globalization;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Replication.Exchange;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>Was ein Abgleich-Durchgang ergeben hat.</summary>
    /// <param name="FinishedUtc">Ende des Durchgangs.</param>
    /// <param name="Received">Was von anderen Geräten übernommen wurde.</param>
    /// <param name="Published">Wie viele eigene Änderungen geschrieben wurden.</param>
    /// <param name="Compacted">Ob das eigene Protokoll verdichtet wurde.</param>
    /// <param name="Error">Warum der Durchgang scheiterte, sonst leer.</param>
    public sealed record ReplicationRun(DateTime FinishedUtc, ReceiveResult Received, int Published, bool Compacted, string? Error);

    /// <summary>
    /// Gleicht im Hintergrund über den gemeinsamen Ordner ab: kurz nach dem Start, danach in festem
    /// Abstand und zusätzlich, sobald sich im Ordner etwas tut. Beim Beenden gehen die eigenen
    /// Änderungen mit einer Frist noch einmal hinaus.
    /// </summary>
    /// <remarks>
    /// Ein Durchgang liest zuerst, dann schreibt er, dann verdichtet er: So enthält ein Gesamtstand
    /// auch das gerade Übernommene. Eigene Änderungen werden gebündelt, nicht einzeln geschrieben;
    /// der Abstand bestimmt, wie lange eine Änderung höchstens auf ihre Datei wartet.
    /// </remarks>
    internal sealed class DeviceReplicationService : IDisposable
    {
        /// <summary>Ab so vielen eigenen Dateien wird das Protokoll zu einem Gesamtstand verdichtet.</summary>
        internal const int CompactThreshold = 50;

        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

        /// <summary>Wartezeit nach einer Meldung des Ordners; ein Cloud-Dienst legt Dateien oft in mehreren Schritten ab.</summary>
        private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeProvider _time;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _runGate = new(1, 1);
        private TaskCompletionSource _wakeSignal = NewSignal();
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private FileSystemWatcher? _watcher;
        private string? _watchedFolder;

        /// <summary>
        /// Initialisiert den Dienst.
        /// </summary>
        /// <param name="scopeFactory">Öffnet je Durchgang einen Bereich mit Datenbankkontext.</param>
        /// <param name="loggerFactory">Die Logger-Fabrik.</param>
        /// <param name="timeProvider">Zeitquelle für Wartezeiten und Ergebnis; ohne Angabe die Systemuhr.</param>
        public DeviceReplicationService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _time = timeProvider ?? TimeProvider.System;
            _logger = loggerFactory.CreateLogger("DeviceReplicationService");
        }

        /// <summary>Nach jedem Durchgang, auch einem gescheiterten.</summary>
        public event Action<ReplicationRun>? RunCompleted;

        /// <summary>Wenn ein Durchgang beginnt oder endet; für Hinweise in der Oberfläche.</summary>
        public event Action? StateChanged;

        /// <summary>Das Ergebnis des letzten Durchgangs, oder leer.</summary>
        public ReplicationRun? LastRun { get; private set; }

        /// <summary>Ob gerade ein Durchgang läuft.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Startet die Schleife; ein zweiter Aufruf tut nichts.</summary>
        public void Start()
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        /// <summary>Löst einen Durchgang sofort aus, etwa über „Jetzt abgleichen“.</summary>
        public void RequestRun() => _ = Volatile.Read(ref _wakeSignal).TrySetResult();

        /// <summary>
        /// Hält die Schleife an und schreibt die eigenen Änderungen ein letztes Mal.
        /// </summary>
        /// <param name="timeout">Frist für Anhalten und Schreiben zusammen.</param>
        /// <returns>Der Task ist abgeschlossen, wenn beides fertig oder die Frist abgelaufen ist.</returns>
        public async Task StopAsync(TimeSpan timeout)
        {
            using CancellationTokenSource deadline = new(timeout, _time);
            if (_cts is not null && _loop is not null)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await _loop.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Erwartet: Die Schleife hat den Abbruch beachtet, oder die Frist ist um.
                }
            }

            try
            {
                _ = await PublishOnlyAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning(
                    "Abgleich beim Beenden nach {Seconds}s abgebrochen; die Änderungen gehen beim nächsten Start hinaus.",
                    timeout.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                _logger.Warning("Abgleich beim Beenden fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>Gibt Schleife und Ordnerüberwachung frei.</summary>
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _loop = null;
            _watcher?.Dispose();
            _watcher = null;
            _runGate.Dispose();
        }

        /// <summary>
        /// Ein Durchgang: lesen, schreiben, verdichten. Ist der Abgleich aus oder der Ordner nicht
        /// erreichbar, geschieht nichts.
        /// </summary>
        /// <param name="cancellationToken">Bricht den Durchgang ab.</param>
        /// <returns>Das Ergebnis, oder leer, wenn nicht abgeglichen wird.</returns>
        public async Task<ReplicationRun?> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                string? folder = await ReadFolderAsync(scope, cancellationToken).ConfigureAwait(false);
                Watch(folder);
                if (folder is null)
                {
                    return null;
                }

                SetRunning(true);
                ReplicationRun run;
                try
                {
                    run = await RunInScopeAsync(scope, folder, cancellationToken).ConfigureAwait(false);
                    LastRun = run;
                }
                finally
                {
                    SetRunning(false);
                }

                RunCompleted?.Invoke(run);
                return run;
            }
            finally
            {
                _ = _runGate.Release();
            }
        }

        private async Task<ReplicationRun> RunInScopeAsync(IServiceScope scope, string folder, CancellationToken cancellationToken)
        {
            DeviceReplicationExchange exchange = scope.ServiceProvider.GetRequiredService<DeviceReplicationExchange>();
            try
            {
                ReceiveResult received = await exchange.ReceiveAsync(folder, cancellationToken).ConfigureAwait(false);
                int published = await exchange.PublishAsync(folder, cancellationToken).ConfigureAwait(false);
                bool compacted = await exchange.CompactAsync(folder, CompactThreshold, cancellationToken).ConfigureAwait(false);
                if (received.Changed > 0 || published > 0)
                {
                    _logger.Info("Abgleich: {Changed} Datensätze übernommen, {Published} eigene Änderungen geschrieben.", received.Changed, published);
                }

                return new ReplicationRun(_time.GetUtcNow().UtcDateTime, received, published, compacted, null);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                _logger.Warning("Abgleich fehlgeschlagen: {Reason}", ex.Message);
                return new ReplicationRun(_time.GetUtcNow().UtcDateTime, new ReceiveResult(0, 0, 0, []), 0, false, ex.Message);
            }
        }

        private void SetRunning(bool running)
        {
            IsRunning = running;
            StateChanged?.Invoke();
        }

        private async Task<int> PublishOnlyAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            string? folder = await ReadFolderAsync(scope, cancellationToken).ConfigureAwait(false);
            return folder is null
                ? 0
                : await scope.ServiceProvider.GetRequiredService<DeviceReplicationExchange>().PublishAsync(folder, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Der gemeinsame Ordner, wenn der Abgleich an ist und der Ordner erreichbar.</summary>
        private static async Task<string?> ReadFolderAsync(IServiceScope scope, CancellationToken cancellationToken)
        {
            AppSettings settings = await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>()
                .GetAsync(cancellationToken).ConfigureAwait(false);
            return settings.ReplicationEnabled && !string.IsNullOrWhiteSpace(settings.ReplicationFolderPath) && Directory.Exists(settings.ReplicationFolderPath)
                ? settings.ReplicationFolderPath
                : null;
        }

        /// <summary>Überwacht den Ordner der anderen Geräte; wechselt der Ordner, wechselt die Überwachung mit.</summary>
        private void Watch(string? folder)
        {
            if (string.Equals(folder, _watchedFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _watcher?.Dispose();
            _watcher = null;
            _watchedFolder = folder;
            if (folder is null)
            {
                return;
            }

            // Ohne Überwachung bleibt der feste Abstand; ein Ordner, der sich nicht überwachen lässt,
            // darf den Abgleich nicht anhalten.
            try
            {
                string changes = Path.Combine(folder, ExchangeLayout.ChangesFolder);
                _ = Directory.CreateDirectory(changes);
                _watcher = new FileSystemWatcher(changes)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                _watcher.Created += (_, _) => RequestRun();
                _watcher.Renamed += (_, _) => RequestRun();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warning("Gemeinsamer Ordner wird nicht überwacht, abgeglichen wird im festen Abstand: {Reason}", ex.Message);
            }
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(InitialDelay, _time, ct).ConfigureAwait(false);
                while (!ct.IsCancellationRequested)
                {
                    _ = await RunOnceAsync(ct).ConfigureAwait(false);
                    await WaitForNextRunAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Erwartet beim Beenden der Anwendung.
            }
        }

        /// <summary>
        /// Wartet bis zum nächsten Durchgang: den festen Abstand, oder kürzer, wenn der Ordner sich
        /// meldet oder jemand „Jetzt abgleichen“ wählt. Eine Meldung während eines Durchgangs geht
        /// nicht verloren; sie löst gleich den nächsten aus.
        /// </summary>
        private async Task WaitForNextRunAsync(CancellationToken ct)
        {
            TaskCompletionSource signal = Volatile.Read(ref _wakeSignal);
            _ = await Task.WhenAny(Task.Delay(PollInterval, _time, ct), signal.Task.WaitAsync(ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (signal.Task.IsCompleted)
            {
                _ = Interlocked.CompareExchange(ref _wakeSignal, NewSignal(), signal);
                await Task.Delay(SettleDelay, _time, ct).ConfigureAwait(false);
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Was ein Durchgang an Fehlern erwarten muss: Ordner, Datenbank, Zugriff.</summary>
        private static bool IsExpected(Exception ex) => ex is IOException
            or UnauthorizedAccessException
            or DbException
            or DbUpdateException
            or InvalidOperationException;
    }
}
