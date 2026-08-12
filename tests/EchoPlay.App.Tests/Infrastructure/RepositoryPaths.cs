using System;
using System.IO;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Findet das Wurzelverzeichnis des Repositories, ausgehend vom Ausgabeverzeichnis des
    /// Testlaufs.
    /// <para>
    /// Hier zusammengezogen, weil dieselbe Aufwärtssuche zuvor in mehreren Testklassen stand.
    /// Der Weg vom Ausgabeverzeichnis zur Quelle hängt von Zielrahmenwerk, Laufzeitkennung und
    /// Konfiguration ab; wird er mehrfach geführt, stimmt nach dem nächsten Umzug nur noch eine
    /// der Kopien — und der Test, der danebenliegt, wird stillschweigend grün, weil er nichts
    /// mehr findet.
    /// </para>
    /// </summary>
    internal static class RepositoryPaths
    {
        /// <summary>
        /// Liefert das Verzeichnis, das <c>EchoPlay.slnx</c> enthält.
        /// </summary>
        /// <returns>Absoluter Pfad des Repository-Wurzelverzeichnisses.</returns>
        /// <exception cref="InvalidOperationException">
        /// Oberhalb des Ausgabeverzeichnisses liegt keine <c>EchoPlay.slnx</c>.
        /// </exception>
        public static string Root()
        {
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? dir = new(baseDir);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EchoPlay.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName
                ?? throw new InvalidOperationException($"EchoPlay.slnx nicht gefunden, ausgehend von '{baseDir}'.");
        }

        /// <summary>
        /// Liefert das Quellverzeichnis des App-Projekts.
        /// </summary>
        /// <returns>Absoluter Pfad zu <c>src/EchoPlay.App</c>.</returns>
        public static string AppProject() => Path.Combine(Root(), "src", "EchoPlay.App");
    }
}
