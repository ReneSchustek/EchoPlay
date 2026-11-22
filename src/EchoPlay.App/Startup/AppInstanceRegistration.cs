using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Logger.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace EchoPlay.App.Startup
{
    /// <summary>
    /// Hält EchoPlay auf einen Prozess. Ein zweiter Start gibt seine Aktivierung an die
    /// laufende Instanz ab und beendet sich, bevor er Datenbank oder Protokoll anfasst.
    /// </summary>
    /// <remarks>
    /// Zwei gleichzeitige Starts räumten sonst dieselbe Datenbank auf und schrieben in dieselbe
    /// Protokolldatei; einer bräche dabei ab. Der Anspruch läuft über
    /// <see cref="AppInstance.FindOrRegisterForKey"/>, das auch ohne Paket-Identität arbeitet.
    /// Die Klasse hält keinen eigenen Zustand, der Anspruch liegt bei Windows. Deshalb
    /// darf der Startlauf eine eigene Instanz anlegen und der Container eine zweite.
    /// </remarks>
    internal sealed class AppInstanceRegistration : IAppInstanceRegistration
    {
        // Gilt pro angemeldetem Benutzer: Zwei Benutzer auf demselben Rechner haben je
        // eigene Datenbank und eigenes Protokoll und stören sich nicht.
        private const string InstanceKey = "EchoPlay.Main";

        /// <summary>
        /// Meldet diesen Prozess als laufende Instanz an. Läuft schon eine, bekommt sie die
        /// Aktivierung dieses Starts und darf ihr Fenster nach vorn holen.
        /// </summary>
        /// <returns>
        /// <see langword="true"/>, wenn dieser Prozess die laufende Instanz ist und weiterstarten
        /// soll; <see langword="false"/>, wenn er sich beenden soll.
        /// </returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Scheitert die Anmeldung bei der Windows-Laufzeit, startet der Prozess als eigene Instanz. Ein fehlender Schutz vor dem Doppelstart ist besser als eine Anwendung, die gar nicht mehr startet.")]
        public async Task<bool> TryBecomePrimaryAsync()
        {
            try
            {
                AppInstance primary = AppInstance.FindOrRegisterForKey(InstanceKey);
                if (primary.IsCurrent)
                {
                    return true;
                }

                // Die Freigabe muss vor der Umleitung stehen: Die laufende Instanz holt ihr
                // Fenster nach vorn, sobald die Aktivierung bei ihr ankommt.
                _ = NativeMethods.AllowSetForegroundWindow(primary.ProcessId);

                AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();

                // Außerhalb des UI-Threads: Die Umleitung ist ein Aufruf in einen anderen
                // Prozess, und auf dem STA-Thread des Fensters kann sie hängen bleiben.
                await Task.Run(async () => await primary.RedirectActivationToAsync(activation));

                // Nur Trace, nicht das Protokoll: In die Protokolldatei schreibt allein die
                // laufende Instanz, und sie vermerkt den umgeleiteten Start selbst.
                EmergencyTrace.Log($"EchoPlay läuft bereits (Prozess {primary.ProcessId}), Start dorthin umgeleitet.");
                return false;
            }
            catch (Exception ex)
            {
                EmergencyTrace.Log($"Prüfung auf eine laufende Instanz fehlgeschlagen, Start läuft weiter: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Ruft <paramref name="onRedirectedLaunch"/> auf dem UI-Thread auf, sobald ein
        /// weiterer Start seine Aktivierung an diesen Prozess abgegeben hat.
        /// </summary>
        /// <param name="dispatcher">Die Warteschlange des UI-Threads; das Ereignis kommt auf einem anderen Thread an.</param>
        /// <param name="onRedirectedLaunch">Was bei einem umgeleiteten Start geschehen soll.</param>
        public static void ListenForRedirectedLaunches(DispatcherQueue dispatcher, Action onRedirectedLaunch)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            ArgumentNullException.ThrowIfNull(onRedirectedLaunch);

            AppInstance.GetCurrent().Activated += (_, _) => _ = dispatcher.TryEnqueue(() => onRedirectedLaunch());
        }

        /// <inheritdoc/>
        public void Release() => AppInstance.GetCurrent().UnregisterKey();

        /// <inheritdoc/>
        public void Reclaim() => _ = AppInstance.FindOrRegisterForKey(InstanceKey);
    }
}
