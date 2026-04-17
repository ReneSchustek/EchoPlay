using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Core;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Stellt die Fatal-Log-Logik fuer globale Exception-Hooks bereit.
    /// Wird von <see cref="App.OnDomainUnhandledException"/> und
    /// <see cref="App.OnUnobservedTaskException"/> aufgerufen. Extrahiert in eine eigene
    /// Klasse, damit die Log-Pfade ohne WinUI-Runtime unit-testbar sind.
    /// </summary>
    internal static class FatalExceptionHandler
    {
        /// <summary>
        /// Loggt eine Exception aus <see cref="AppDomain.UnhandledException"/>. Faellt auf
        /// <see cref="EmergencyTrace"/> zurueck, wenn der regulaere Logger noch nicht verfuegbar ist.
        /// </summary>
        /// <param name="logger">Der regulaere App-Logger, <see langword="null"/> vor DI-Init oder nach Dispose.</param>
        /// <param name="e">Event-Argument des AppDomain-Hooks, enthaelt <c>ExceptionObject</c> und <c>IsTerminating</c>.</param>
        public static void HandleDomainException(ILogger? logger, UnhandledExceptionEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            string marker = e.IsTerminating ? "TERMINATING" : "NON-TERMINATING";
            Exception? exception = e.ExceptionObject as Exception;
            string message = $"AppDomain.UnhandledException ({marker}): {exception?.Message ?? e.ExceptionObject}";

            if (logger is not null)
            {
                logger.Fatal(message, exception);
            }
            else
            {
                EmergencyTrace.Log($"[FATAL AppDomain {marker}] {e.ExceptionObject}");
            }
        }

        /// <summary>
        /// Loggt eine Exception aus <see cref="TaskScheduler.UnobservedTaskException"/> und markiert sie
        /// als beobachtet, damit der Prozess nicht vom TaskScheduler abgebrochen wird.
        /// </summary>
        /// <param name="logger">Der regulaere App-Logger, <see langword="null"/> vor DI-Init oder nach Dispose.</param>
        /// <param name="e">Event-Argument des TaskScheduler-Hooks, enthaelt <c>Exception</c>.</param>
        public static void HandleUnobservedTaskException(ILogger? logger, UnobservedTaskExceptionEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);

            string message = $"UnobservedTaskException: {e.Exception.Message}";
            if (logger is not null)
            {
                logger.Error(message, e.Exception);
            }
            else
            {
                EmergencyTrace.Log($"[ERROR UnobservedTaskException] {e.Exception}");
            }

            e.SetObserved();
        }
    }
}
