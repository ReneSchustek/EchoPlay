using EchoPlay.App.Models;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Koordiniert die Fehlende-Folgen-Prüfung für eine einzelne Serie und für die
    /// gesamte Bibliothek. Kapselt die Datei-System-Analyse, den Live-Online-Abgleich
    /// per iTunes und die Status-Bar-Aktualisierung – das ViewModel sieht nur zwei
    /// Eintrittspunkte und reicht den Modus aus dem UI-Dialog durch.
    /// </summary>
    public interface IMissingEpisodesCoordinator
    {
        /// <summary>
        /// Prüft eine einzelne Serie auf fehlende Folgen. Liefert eine Liste von
        /// Anzeigemeldungen für den Dialog. Bei Cancel oder unbekanntem Ordner
        /// wird eine erklärende Einzelmeldung zurückgegeben.
        /// </summary>
        /// <param name="seriesId">ID der Serie (für den iTunes-Abgleich).</param>
        /// <param name="seriesFolderPath">Absoluter Pfad des Serienordners – darf <see langword="null"/> sein.</param>
        /// <param name="mode">Vom Nutzer im Drei-Optionen-Dialog gewählter Prüfmodus.</param>
        Task<IReadOnlyList<string>> CheckSingleSeriesAsync(
            Guid seriesId,
            string? seriesFolderPath,
            MissingEpisodesMode mode);

        /// <summary>
        /// Prüft alle abonnierten Serien mit lokalem Ordner. Liefert einen strukturierten
        /// Bericht für die Anzeige im Gesamtprüf-Dialog.
        /// </summary>
        /// <param name="mode">Modus aus dem Drei-Optionen-Dialog.</param>
        Task<MissingEpisodesReport> CheckAllSeriesAsync(MissingEpisodesMode mode);
    }
}
