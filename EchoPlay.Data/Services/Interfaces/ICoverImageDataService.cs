using EchoPlay.Data.Entities.Library;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Zugriff auf Cover-Binärdaten in der separaten CoverImages-Tabelle.
    /// Trennt schwere Blob-Daten von den leichtgewichtigen Metadaten-Tabellen.
    /// </summary>
    public interface ICoverImageDataService
    {
        /// <summary>
        /// Lädt das Cover einer einzelnen Entity (Serie oder Episode).
        /// </summary>
        /// <param name="entityType">"Series" oder "Episode".</param>
        /// <param name="entityId">ID der verknüpften Entity.</param>
        /// <returns>Das Cover oder null wenn keins vorhanden.</returns>
        Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId);

        /// <summary>
        /// Lädt Cover für mehrere Entities in einer Query (Batch).
        /// Verhindert N+1-Probleme beim Laden von Serien-/Episodenlisten.
        /// </summary>
        /// <param name="entityType">"Series" oder "Episode".</param>
        /// <param name="entityIds">IDs der verknüpften Entities.</param>
        /// <returns>Dictionary von EntityId auf Cover-Binärdaten.</returns>
        Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(
            string entityType, IReadOnlyList<Guid> entityIds);

        /// <summary>
        /// Speichert oder aktualisiert ein Cover für eine Entity.
        /// Existiert bereits ein Cover, werden Bilddaten und SourceUrl überschrieben.
        /// </summary>
        Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, string? sourceUrl = null);

        /// <summary>
        /// Setzt den LastChecked-Zeitstempel für eine Entity (auch bei Nicht-Treffer).
        /// </summary>
        Task SetLastCheckedAsync(string entityType, Guid entityId, DateTime checkedAt);

        /// <summary>
        /// Liefert Entities, deren Cover noch nie geprüft wurde oder deren Cooldown abgelaufen ist.
        /// Für den Background-Worker: gibt die nächsten zu prüfenden Entities zurück.
        /// </summary>
        /// <param name="entityType">"Series" oder "Episode".</param>
        /// <param name="cooldownThreshold">Zeitpunkt, vor dem LastChecked liegen muss (oder NULL).</param>
        /// <param name="limit">Maximale Anzahl Ergebnisse.</param>
        /// <returns>Entity-IDs die geprüft werden sollen.</returns>
        Task<IReadOnlyList<Guid>> GetUncheckedEntityIdsAsync(
            string entityType, DateTime cooldownThreshold, int limit);

        /// <summary>
        /// Prüft ob ein Cover für eine Entity existiert.
        /// Schneller als GetByEntityAsync, da kein Blob geladen wird.
        /// </summary>
        Task<bool> ExistsAsync(string entityType, Guid entityId);

        /// <summary>
        /// Löscht alle Cover-Einträge aus der Tabelle.
        /// Wird beim Cache-Reset aufgerufen – der BackgroundCoverService baut die Cover danach neu auf.
        /// </summary>
        /// <returns>Anzahl der gelöschten Einträge.</returns>
        Task<int> ClearAllAsync();

    }
}
