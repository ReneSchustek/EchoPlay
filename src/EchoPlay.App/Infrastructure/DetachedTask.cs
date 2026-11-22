using EchoPlay.Logger.Abstractions;
using System.Runtime.CompilerServices;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Beobachtet Aufgaben, die nebenher laufen und auf die niemand wartet.
    /// <para>
    /// Wird ein Hintergrundabruf mit <c>_ = ...Async(...)</c> gestartet und sein Ergebnis
    /// verworfen, bricht beim Beenden der Abbruch-Token diese Aufgabe ab. Die Abbruch-Ausnahme
    /// bliebe unbeobachtet und tauchte erst auf, wenn der Speicherbereiniger die Aufgabe
    /// einsammelt, wenn die Anwendung ihre Ausnahme-Behandlung schon abgemeldet hat. Der Nutzer
    /// sähe eine <see cref="TaskCanceledException"/> beim Schließen, obwohl nichts schiefging.
    /// </para>
    /// <para>
    /// Ein Abbruch ist hier kein Fehler, sondern der Normalfall beim Beenden. Er
    /// wird geschluckt. Alles andere wird protokolliert, damit ein echter Fehler im
    /// Hintergrund nicht spurlos verschwindet.
    /// </para>
    /// </summary>
    internal static class DetachedTask
    {
        /// <summary>
        /// Nimmt eine nebenher laufende Aufgabe entgegen und beobachtet ihren Ausgang.
        /// </summary>
        /// <param name="task">Die bereits gestartete Aufgabe.</param>
        /// <param name="logger">Protokoll, in dem ein echter Fehler landet.</param>
        /// <param name="operationName">
        /// Name der Operation; wird ohne Angabe vom Aufrufer übernommen.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> oder <paramref name="logger"/> ist <see langword="null"/>.
        /// </exception>
        public static void Observe(
            Task task,
            ILogger logger,
            [CallerMemberName] string operationName = "")
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(logger);

            _ = task.ContinueWith(
                finished => ReportFailure(finished, logger, operationName),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Protokolliert die Fehler einer beendeten Aufgabe; Abbrüche bleiben stumm.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sicherheitsnetz für losgelöste Hintergrund-Aufgaben: Ohne diesen Fang bliebe die Ausnahme unbeobachtet und schlüge erst beim Einsammeln durch den Speicherbereiniger auf.")]
        private static void ReportFailure(Task finished, ILogger logger, string operationName)
        {
            if (finished.IsCanceled)
            {
                // Beim Beenden erwartet, daher keine Meldung.
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

                foreach (Exception inner in error.Flatten().InnerExceptions.Where(inner => inner is not OperationCanceledException))
                {
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
        }
    }
}
