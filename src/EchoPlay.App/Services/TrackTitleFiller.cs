using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Eine Zeile einer Trackliste, deren Titel nachgeladen werden kann.
    /// </summary>
    internal interface ITrackTitleTarget
    {
        /// <summary>Absoluter Pfad zur Audiodatei.</summary>
        string FilePath { get; }

        /// <summary>Nummer, die in der Liste bereits in eigener Spalte steht.</summary>
        int TrackNumber { get; }

        /// <summary>Übernimmt den gelesenen Titel.</summary>
        /// <param name="resolvedTitle">Der gelesene Titel oder <see langword="null"/>.</param>
        void ApplyTitle(string? resolvedTitle);
    }

    /// <summary>
    /// Trägt die Titel aus den Kennzeichnungen in eine bereits angezeigte Trackliste nach.
    /// Die Liste steht sofort mit dem Dateinamen da; die Titel kommen, sobald die Dateien
    /// gelesen sind. Alle drei Tracklisten der Anwendung nutzen denselben Weg.
    /// </summary>
    internal static class TrackTitleFiller
    {
        /// <summary>
        /// Liest die Titel der übergebenen Zeilen und trägt sie ein.
        /// Wird der Vorgang abgebrochen, bleibt der Dateiname stehen.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für den Scope des Auflösers.</param>
        /// <param name="rows">Die angezeigten Zeilen in Anzeigereihenfolge.</param>
        /// <param name="cancellationToken">Bricht das Nachladen ab.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Titel eingetragen sind.</returns>
        public static async Task FillAsync(
            IServiceScopeFactory scopeFactory,
            IReadOnlyList<ITrackTitleTarget> rows,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(rows);

            if (rows.Count == 0)
            {
                return;
            }

            List<TrackTitleRequest> requests = new(rows.Count);

            foreach (ITrackTitleTarget row in rows)
            {
                requests.Add(new TrackTitleRequest(row.FilePath, row.TrackNumber));
            }

            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ITrackTitleResolver resolver = scope.ServiceProvider.GetRequiredService<ITrackTitleResolver>();

                // Bewusst ohne ConfigureAwait(false): Die Fortsetzung schreibt in gebundene
                // Eigenschaften und muss deshalb auf dem UI-Thread laufen.
                IReadOnlyList<string> titles = await resolver.ResolveAsync(requests, cancellationToken);

                for (int i = 0; i < rows.Count && i < titles.Count; i++)
                {
                    rows[i].ApplyTitle(titles[i]);
                }
            }
            catch (OperationCanceledException)
            {
                // Folge gewechselt oder Seite verlassen – der Dateiname bleibt stehen.
            }
        }
    }
}
