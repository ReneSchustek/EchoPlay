namespace EchoPlay.App.Services
{
    /// <summary>
    /// Startet die heruntergeladene Setup-Datei eines Updates.
    /// </summary>
    /// <remarks>
    /// Bewusst getrennt von <see cref="IProcessLauncher"/>: Der dortige Namensfilter lässt
    /// ausschließlich <c>EchoPlay.App</c> zu und passt deshalb nicht auf den Installer.
    /// Ohne eigene Schnittstelle rief der Update-Pfad <c>Process.Start</c> direkt auf — ein
    /// Test, der bis dorthin lief, startete eine echte ausführbare Datei aus <c>%TEMP%</c>.
    /// Über diese Abstraktion bekommen Tests ein Fake und lösen nie einen echten Start aus.
    /// </remarks>
    public interface IInstallerLauncher
    {
        /// <summary>
        /// Startet die Setup-Datei über die Shell.
        /// </summary>
        /// <param name="setupPath">Vollständiger Pfad der geprüften Setup-Datei.</param>
        /// <returns><see langword="true"/>, wenn der Start ausgelöst werden konnte.</returns>
        bool Start(string setupPath);
    }
}
