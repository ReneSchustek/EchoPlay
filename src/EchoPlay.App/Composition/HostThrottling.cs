namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Ob ein HTTP-Client die Ratenbremse je Gegenstelle durchläuft. Gedrosselt werden die
    /// Anbieter, die bei zu vielen Abfragen sperren; eigene Downloads und Bild-Server nicht.
    /// </summary>
    internal enum HostThrottling
    {
        /// <summary>Ohne Ratenbremse.</summary>
        None,

        /// <summary>Mit der Ratenbremse je Gegenstelle.</summary>
        PerHost,
    }
}
