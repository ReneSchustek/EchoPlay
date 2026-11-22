namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Der Pfad der Datenbankdatei als eigener Typ, damit Dienste, die die Datei selbst anfassen
    /// (Sicherung, Snapshot), ihn per Konstruktor bekommen statt ihn aus dem Verbindungsstring
    /// des Kontexts zurückzulesen.
    /// </summary>
    /// <remarks>
    /// Die Quelle ist <see cref="DatabasePathProvider"/>. Der Rückweg über
    /// <c>DbContext.Database</c> hätte denselben Wert geliefert, gilt der Sicherheitsanalyse aber
    /// als Datenbankinhalt und damit als fremdgesteuert; jede Dateioperation darauf wäre ein
    /// Pfad-Traversal-Befund.
    /// </remarks>
    /// <param name="FilePath">Absoluter Pfad der SQLite-Datei.</param>
    public sealed record DatabaseFileLocation(string FilePath);
}
