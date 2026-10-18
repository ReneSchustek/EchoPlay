namespace EchoPlay.App.Services
{
    /// <summary>
    /// Startet externe Prozesse. Existiert, damit Code, der die Anwendung neu startet,
    /// in Tests nicht wirklich einen Prozess erzeugt.
    /// </summary>
    /// <remarks>
    /// Hintergrund: Ein Test, der den Neustart-Pfad direkt aufruft, startet den Testhost
    /// statt der App und vervielfältigt sich damit selbst.
    /// Über diese Schnittstelle bekommen Tests ein Fake und lösen nie einen echten Start aus.
    /// </remarks>
    public interface IProcessLauncher
    {
        /// <summary>Pfad der laufenden ausführbaren Datei, oder <see langword="null"/> wenn unbekannt.</summary>
        string? CurrentExecutablePath { get; }

        /// <summary>
        /// Startet die angegebene ausführbare Datei über die Shell.
        /// </summary>
        /// <param name="executablePath">Vollständiger Pfad der zu startenden Datei.</param>
        /// <returns><see langword="true"/>, wenn der Start ausgelöst werden konnte.</returns>
        bool Start(string executablePath);
    }
}
