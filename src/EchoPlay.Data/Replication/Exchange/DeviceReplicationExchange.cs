using System.Text;
using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Replication.Exchange
{
    /// <summary>Was ein Lesen des gemeinsamen Ordners ergeben hat.</summary>
    /// <param name="Files">Übernommene Dateien.</param>
    /// <param name="Records">Gelesene Sätze.</param>
    /// <param name="Changed">Datensätze, die sich dadurch hier geändert haben.</param>
    /// <param name="Problems">Je Gerät, warum das Lesen stehen blieb; leer, wenn alles lesbar war.</param>
    public sealed record ReceiveResult(int Files, int Records, int Changed, IReadOnlyList<string> Problems);

    /// <summary>
    /// Der Abgleich über einen gemeinsamen Ordner: eigene Änderungen schreiben, fremde übernehmen,
    /// das eigene Protokoll verdichten.
    /// </summary>
    /// <remarks>
    /// Jedes Gerät schreibt nur in seinen eigenen Unterordner, jede Datei einmal und unter einem
    /// vorläufigen Namen, der erst am Ende umbenannt wird. Gelesen wird je Gerät ab dem eigenen
    /// Lesestand; eine beschädigte, abgeschnittene oder fremde Datei hält das Lesen dieses Geräts an
    /// (sie kann noch übertragen werden), ohne den Bestand anzufassen, und steht im Ergebnis.
    /// </remarks>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="device">Dieses Gerät.</param>
    /// <param name="clock">Die logische Uhr.</param>
    /// <param name="time">Zeitquelle für Lesestand und Löschzeitpunkte.</param>
    /// <param name="loggerFactory">Die Logger-Fabrik.</param>
    public sealed class DeviceReplicationExchange(
        EchoPlayDbContext context,
        IDeviceIdentity device,
        IHybridClock clock,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger("DeviceReplication");

        /// <summary>
        /// Schreibt die eigenen Änderungen seit dem letzten Schreiben als neue Datei.
        /// </summary>
        /// <param name="exchangeRoot">Der gemeinsame Ordner.</param>
        /// <param name="cancellationToken">Bricht ab; eine halbe Datei bleibt unter vorläufigem Namen und wird übergangen.</param>
        /// <returns>Die Zahl der geschriebenen Sätze; 0, wenn es nichts Neues gab.</returns>
        public async Task<int> PublishAsync(string exchangeRoot, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exchangeRoot);
            ReplicationPeer own = await PeerAsync(device.DeviceId, cancellationToken).ConfigureAwait(false);
            LocalReplica replica = await LocalReplica.LoadAsync(context, cancellationToken).ConfigureAwait(false);

            HybridTimestamp exported = HybridTimestamp.TryParse(own.ExportedStamp, out HybridTimestamp stamp) ? stamp : LocalReplica.Unstamped;
            List<ChangeRecord> changes = [.. replica.Records.Where(r => r.Stamp.DeviceId == device.DeviceId && r.Stamp > exported)];
            if (changes.Count == 0)
            {
                return 0;
            }

            long sequence = own.LastSequence + 1;
            await WriteAsync(exchangeRoot, new ChangeBatch(ChangeBatch.CurrentFormatVersion, device.DeviceId, sequence, false, changes), cancellationToken).ConfigureAwait(false);
            own.LastSequence = sequence;
            own.ExportedStamp = changes.Max(r => r.Stamp).ToString();
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("Abgleich: {Count} eigene Änderungen als Datei {Sequence} geschrieben.", changes.Count, sequence);
            return changes.Count;
        }

        /// <summary>
        /// Übernimmt, was die anderen Geräte seit dem letzten Lesen geschrieben haben.
        /// </summary>
        /// <param name="exchangeRoot">Der gemeinsame Ordner.</param>
        /// <param name="cancellationToken">Bricht ab; schon übernommene Dateien bleiben übernommen.</param>
        /// <returns>Was gelesen und übernommen wurde.</returns>
        public async Task<ReceiveResult> ReceiveAsync(string exchangeRoot, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exchangeRoot);
            string changesRoot = Path.Combine(exchangeRoot, ExchangeLayout.ChangesFolder);
            if (!Directory.Exists(changesRoot))
            {
                return new ReceiveResult(0, 0, 0, []);
            }

            int files = 0;
            int records = 0;
            int changed = 0;
            List<string> problems = [];
            foreach (string folder in Directory.EnumerateDirectories(changesRoot).Order(StringComparer.Ordinal))
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid peer) || peer == device.DeviceId)
                {
                    continue;
                }

                (int Files, int Records, int Changed, string? Problem) result = await ReceiveFromAsync(folder, peer, cancellationToken).ConfigureAwait(false);
                files += result.Files;
                records += result.Records;
                changed += result.Changed;
                if (result.Problem is not null)
                {
                    problems.Add($"{peer:N}: {result.Problem}");
                }
            }

            return new ReceiveResult(files, records, changed, problems);
        }

        /// <summary>
        /// Ersetzt das eigene Protokoll durch einen Gesamtstand, sobald es <paramref name="threshold"/>
        /// Dateien hat, und löscht danach die eigenen älteren Dateien. Fremde Dateien bleiben unberührt.
        /// </summary>
        /// <param name="exchangeRoot">Der gemeinsame Ordner.</param>
        /// <param name="threshold">Ab wie vielen eigenen Dateien verdichtet wird.</param>
        /// <param name="cancellationToken">Bricht ab; vor dem Schreiben des Gesamtstands wird nichts gelöscht.</param>
        /// <returns><see langword="true"/>, wenn verdichtet wurde.</returns>
        public async Task<bool> CompactAsync(string exchangeRoot, int threshold, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exchangeRoot);
            ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);
            string folder = ExchangeLayout.DeviceFolder(exchangeRoot, device.DeviceId);
            List<ExchangeFile> existing = ListFiles(folder);
            if (existing.Count(f => !f.IsSnapshot) < threshold)
            {
                return false;
            }

            // Zuerst die eigenen Änderungen hinaus, damit der Gesamtstand alles bis zu seiner Nummer enthält.
            _ = await PublishAsync(exchangeRoot, cancellationToken).ConfigureAwait(false);
            ReplicationPeer own = await PeerAsync(device.DeviceId, cancellationToken).ConfigureAwait(false);
            LocalReplica replica = await LocalReplica.LoadAsync(context, cancellationToken).ConfigureAwait(false);
            await WriteAsync(exchangeRoot, new ChangeBatch(ChangeBatch.CurrentFormatVersion, device.DeviceId, own.LastSequence, true, [.. replica.Records]), cancellationToken).ConfigureAwait(false);

            foreach (ExchangeFile old in ListFiles(folder).Where(f => f.Sequence < own.LastSequence || (f.Sequence == own.LastSequence && !f.IsSnapshot)))
            {
                File.Delete(old.Path);
            }

            _logger.Info("Abgleich: eigenes Protokoll zu einem Gesamtstand bei Nummer {Sequence} verdichtet.", own.LastSequence);
            return true;
        }

        private async Task<(int Files, int Records, int Changed, string? Problem)> ReceiveFromAsync(string folder, Guid peer, CancellationToken cancellationToken)
        {
            ReplicationPeer state = await PeerAsync(peer, cancellationToken).ConfigureAwait(false);

            // Ein in den Einstellungen entferntes Gerät wird nicht mehr gelesen.
            if (state.IsDeleted)
            {
                return (0, 0, 0, null);
            }
            int files = 0;
            int records = 0;
            int changed = 0;
            string? problem = null;
            IReadOnlyList<ExchangeFile> pending = ExchangeLayout.PendingAfter(ListFiles(folder), state.LastSequence);
            ReplicaApplier? applier = null;
            foreach (ExchangeFile file in pending)
            {
                (ChangeBatch? read, string? error) = await ReadAsync(file, peer).ConfigureAwait(false);
                if (read is null)
                {
                    problem = $"Datei {Path.GetFileName(file.Path)}: {error}";
                    break;
                }

                // Einmal je Gerät geladen; der Übernehmer führt den Bestand selbst nach.
                applier ??= new ReplicaApplier(context, await LocalReplica.LoadAsync(context, cancellationToken).ConfigureAwait(false), clock, time, _logger);
                changed += await ApplyFileAsync(applier, state, file.Sequence, read, cancellationToken).ConfigureAwait(false);
                files++;
                records += read.Records.Count;
            }

            await RecordProblemAsync(state, peer, problem, cancellationToken).ConfigureAwait(false);
            return (files, records, changed, problem);
        }

        /// <summary>
        /// Übernimmt eine Datei und schreibt den Stand des Geräts gleich mit, damit ein Abbruch
        /// danach dieselbe Datei nicht noch einmal liest.
        /// </summary>
        /// <returns>Die Zahl der geänderten Zeilen.</returns>
        private async Task<int> ApplyFileAsync(
            ReplicaApplier applier,
            ReplicationPeer state,
            long sequence,
            ChangeBatch read,
            CancellationToken cancellationToken)
        {
            int changed = applier.Apply(read.Records);
            state.LastSequence = sequence;
            state.LastSeenUtc = time.GetUtcNow().UtcDateTime;
            state.AppliedRecords += read.Records.Count;
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return changed;
        }

        private async Task RecordProblemAsync(ReplicationPeer state, Guid peer, string? problem, CancellationToken cancellationToken)
        {
            state.LastError = problem;
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (problem is not null)
            {
                _logger.Warning("Abgleich: Lesen von Gerät {Device} angehalten: {Problem}", peer.ToString("N"), problem);
            }
        }

        private static async Task<(ChangeBatch? Batch, string? Problem)> ReadAsync(ExchangeFile file, Guid peer)
        {
            string json;
            try
            {
                json = await File.ReadAllTextAsync(file.Path, new UTF8Encoding(false, throwOnInvalidBytes: true)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                return (null, ex.Message);
            }

            if (!ChangeBatch.TryParse(json, out ChangeBatch? batch, out string? error))
            {
                return (null, error);
            }

            return batch.DeviceId != peer || batch.Sequence != file.Sequence || batch.IsSnapshot != file.IsSnapshot
                ? (null, "Inhalt passt nicht zu Ordner und Dateiname; die Datei gehört nicht hierher.")
                : (batch, null);
        }

        private async Task WriteAsync(string exchangeRoot, ChangeBatch batch, CancellationToken cancellationToken)
        {
            string folder = ExchangeLayout.DeviceFolder(exchangeRoot, batch.DeviceId);
            _ = Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, ExchangeLayout.FileName(batch.Sequence, batch.IsSnapshot));
            string partial = target + ExchangeLayout.PartialSuffix;
            await File.WriteAllTextAsync(partial, batch.ToJson(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(partial, target, overwrite: true);
        }

        private static List<ExchangeFile> ListFiles(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            List<ExchangeFile> files = [];
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                if (ExchangeLayout.TryRead(path, out ExchangeFile? file))
                {
                    files.Add(file);
                }
            }

            return files;
        }

        /// <summary>Die Zeile eines Geräts; fehlt sie, wird sie angelegt.</summary>
        private async Task<ReplicationPeer> PeerAsync(Guid id, CancellationToken cancellationToken)
        {
            ReplicationPeer? peer = await context.ReplicationPeers.AsTracking().FirstOrDefaultAsync(p => p.DeviceId == id, cancellationToken).ConfigureAwait(false);
            if (peer is null)
            {
                peer = new ReplicationPeer { DeviceId = id };
                _ = context.ReplicationPeers.Add(peer);
            }

            return peer;
        }
    }
}
