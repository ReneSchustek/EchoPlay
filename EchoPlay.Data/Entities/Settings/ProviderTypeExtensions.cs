namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Erweiterungsmethoden für <see cref="ProviderType"/>. Ermöglicht die Prüfung,
    /// ob ein bestimmter Anbieter im gewählten Modus enthalten ist — besonders relevant
    /// für <see cref="ProviderType.Both"/>, der sowohl Apple Music als auch Spotify einschließt.
    /// </summary>
    public static class ProviderTypeExtensions
    {
        /// <summary>
        /// Prüft, ob der angegebene Provider im aktuellen Wert enthalten ist.
        /// <see cref="ProviderType.Both"/> schließt sowohl Spotify als auch Apple Music ein.
        /// </summary>
        public static bool Includes(this ProviderType value, ProviderType check)
            => value == check
               || (value == ProviderType.Both && check is ProviderType.AppleMusic or ProviderType.Spotify);
    }
}
