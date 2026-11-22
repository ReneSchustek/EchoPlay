using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;

namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Ein Gerät im Abgleich über den gemeinsamen Ordner und wie weit dieses Gerät dessen Protokoll
    /// übernommen hat. Auch das eigene Gerät hat eine Zeile: Dort steht, wie weit es selbst
    /// geschrieben hat.
    /// </summary>
    /// <remarks>
    /// Die Spalten sind ein Lesestand und als <see cref="DataScope.Cache"/> eingeteilt: Gingen sie
    /// verloren, läse das Gerät alles noch einmal, und das Zusammenführen ergäbe dasselbe. Kommen sie
    /// mit einem Datenpaket von einem anderen Gerät, stimmen sie auch dort, denn das Paket enthält
    /// genau den Stand, den sie beschreiben.
    /// </remarks>
    public sealed class ReplicationPeer : BaseEntity
    {
        /// <summary>Geräte-Kennung, eindeutig.</summary>
        [DataScope(DataScope.Cache)]
        public Guid DeviceId { get; set; }

        /// <summary>Höchste übernommene Nummer, beim eigenen Gerät die höchste geschriebene.</summary>
        [DataScope(DataScope.Cache)]
        public long LastSequence { get; set; }

        /// <summary>Beim eigenen Gerät: der jüngste schon geschriebene Änderungszeitpunkt.</summary>
        [DataScope(DataScope.Cache)]
        public string? ExportedStamp { get; set; }

        /// <summary>Wann zuletzt eine Datei dieses Geräts übernommen wurde (UTC).</summary>
        [DataScope(DataScope.Cache)]
        public DateTime? LastSeenUtc { get; set; }

        /// <summary>Wie viele Sätze dieses Geräts bisher übernommen wurden.</summary>
        [DataScope(DataScope.Cache)]
        public long AppliedRecords { get; set; }

        /// <summary>Warum das Lesen zuletzt stehen blieb, oder leer.</summary>
        [DataScope(DataScope.Cache)]
        public string? LastError { get; set; }
    }
}
