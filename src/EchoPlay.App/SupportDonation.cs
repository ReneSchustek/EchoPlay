using System;

namespace EchoPlay.App
{
    /// <summary>
    /// Fest verdrahtetes Spendenziel für den freiwilligen „Kaffee spendieren"-Eintrag.
    /// <para>
    /// Die Ziel-URL ist bewusst eine Compile-Zeit-Konstante und wird <b>niemals</b> aus
    /// <c>appsettings</c>, Datenbank, Umgebungsvariable oder Netz-Antwort gelesen — so gibt es
    /// zur Laufzeit keinen austauschbaren Wert, über den jemand das Geld umleiten könnte.
    /// Die Manipulationssicherheit des Distributionswegs liefert das SHA-256-Hash-Pinning der
    /// Release-Binary (eine getauschte URL ändert den Hash und fällt bei der Prüfung durch).
    /// </para>
    /// </summary>
    internal static class SupportDonation
    {
        /// <summary>
        /// Platzhalter-Segment im noch nicht konfigurierten Link. Solange es im
        /// <see cref="PayPalUrl"/> steht, bleibt der Spenden-Eintrag ausgeblendet
        /// (<see cref="IsConfigured"/>), damit nie ein toter Link erscheint.
        /// </summary>
        private const string Placeholder = "PLATZHALTER";

        /// <summary>
        /// Offizielle PayPal-Spenden-URL (HTTPS, ohne Laufzeit-Parameter).
        /// Zum Aktivieren <c>PLATZHALTER</c> durch den echten paypal.me-Handle ersetzen,
        /// z.B. <c>https://paypal.me/ruhrcoder</c>. Dies ist die einzige Stelle, die dafür
        /// angepasst werden muss.
        /// </summary>
        public const string PayPalUrl = "https://paypal.me/" + Placeholder;

        /// <summary>
        /// <see langword="true"/>, sobald ein echter Handle eingetragen ist (kein Platzhalter mehr).
        /// Steuert die Sichtbarkeit des Spenden-Eintrags.
        /// </summary>
        public static bool IsConfigured => !PayPalUrl.Contains(Placeholder, StringComparison.Ordinal);
    }
}
