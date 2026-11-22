namespace EchoPlay.App
{
    /// <summary>
    /// Fest verdrahtetes Spendenziel für den freiwilligen „Kaffee spendieren"-Eintrag.
    /// <para>
    /// Die Ziel-URL ist eine Compile-Zeit-Konstante und wird nie aus
    /// <c>appsettings</c>, Datenbank, Umgebungsvariable oder Netz-Antwort gelesen, so gibt es
    /// zur Laufzeit keinen austauschbaren Wert, über den jemand das Geld umleiten könnte.
    /// Die Manipulationssicherheit des Distributionswegs liefern Signatur und SHA-256 der
    /// Setup-Datei: Eine getauschte URL ändert die Datei, und ohne den privaten Release-Schlüssel
    /// entsteht für die geänderte Datei keine gültige Signatur.
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
        /// Fest verdrahtet auf den paypal.me-Handle des Entwicklers.
        /// </summary>
        public const string PayPalUrl = "https://paypal.me/rschustek";

        /// <summary>
        /// <see langword="true"/>, sobald ein echter Handle eingetragen ist (kein Platzhalter mehr).
        /// Steuert die Sichtbarkeit des Spenden-Eintrags.
        /// </summary>
        public static bool IsConfigured => !PayPalUrl.Contains(Placeholder, StringComparison.Ordinal);
    }
}
