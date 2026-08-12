using EchoPlay.Logger.Abstractions;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Beobachtet Aufgaben, die bewusst nebenher laufen und auf die niemand wartet.
    /// <para>
    /// Anlass: Ein Hintergrundabruf wurde mit <c>_ = ...Async(...)</c> gestartet und sein
    /// Ergebnis verworfen. Beim Beenden der Anwendung bricht der Abbruch-Token diese Aufgabe
    /// ab; die Abbruch-Ausnahme bleibt dann unbeobachtet und taucht später auf, wenn der
    /// Speicherbereiniger die Aufgabe einsammelt — zu einem Zeitpunkt, an dem die Anwendung
    /// ihre Ausnahme-Behandlung längst abgemeldet hat. Der Nutzer sieht eine
    /// <see cref="TaskCanceledException"/> beim Schließen, obwohl nichts schiefgegangen ist.
    /// </para>
    /// <para>
    /// Ein Abbruch ist hier <em>kein</em> Fehler, sondern der Normalfall beim Beenden — er
    /// wird geschluckt. Alles andere wird protokolliert, damit ein echter Fehler im
    /// Hintergrund nicht spurlos verschwindet.
    /// </para>
    /// </summary>
    internal static class DetachedTask
    {
        /// <summary>
        /// Nimmt eine nebenher laufende Aufgabe entgegen und stellt sicher, dass ihr Ausgang
        /// beobachtet wird.
        /// </summary>
        /// <param name="task">Die bereits gestartete Aufgabe.</param>
        /// <param name="logger">Protokoll, in dem ein echter Fehler landet.</param>
        /// <param name="operationName">
        /// Name der Operation; wird ohne Angabe vom Aufrufer übernommen.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> oder <paramref name="logger"/> ist <see langword="null"/>.
        /// </exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sicherheitsnetz für losgelöste Hintergrund-Aufgaben: Ohne diesen Fang bliebe die Ausnahme unbeobachtet und schlüge erst beim Einsammeln durch den Speicherbereiniger auf.")]
        public static void Observe(
            Task task,
            ILogger logger,
            [CallerMemberName] string operationName = "")
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(logger);

            _ = task.ContinueWith(
                finished =>
                {
                    if (finished.IsCanceled)
                    {
                        // Beim Beenden erwartet — keine Meldung.
                        return;
                    }

                    try
                    {
                        // Zugriff auf Exception markiert die Aufgabe als beobachtet.
                        AggregateException? error = finished.Exception;
                        if (error is null)
                        {
                            return;
                        }

                        foreach (Exception inner in error.Flatten().InnerExceptions)
                        {
                            if (inner is OperationCanceledException)
                            {
                                continue;
                            }

                            logger.Warning(
                                "Hintergrund-Aufgabe '{OperationName}' fehlgeschlagen: {Reason}",
                                operationName,
                                inner.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(
                            "Hintergrund-Aufgabe '{OperationName}' konnte nicht ausgewertet werden: {Reason}",
                            operationName,
                            ex.Message);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
