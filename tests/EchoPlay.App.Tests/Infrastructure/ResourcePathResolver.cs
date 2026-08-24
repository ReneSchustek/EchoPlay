using System;
using System.IO;
using System.Linq;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Findet die Sprachdateien der Anwendung im Quellbaum.
    /// </summary>
    /// <remarks>
    /// Sucht ausgehend vom Wurzelverzeichnis, statt das Ordner-Layout fest zu verdrahten —
    /// so übersteht die Suche einen Umzug der Projekte.
    /// </remarks>
    internal static class ResourcePathResolver
    {
        /// <summary>
        /// Liefert den Pfad zur <c>Resources.resw</c> der angegebenen Sprache.
        /// </summary>
        /// <param name="culture">Der Ordnername der Sprache, etwa <c>de</c> oder <c>en-US</c>.</param>
        /// <returns>Der vollständige Pfad zur Sprachdatei.</returns>
        /// <exception cref="FileNotFoundException">Die Datei liegt nicht im Quellbaum.</exception>
        public static string Resolve(string culture)
        {
            string relativePath = Path.Combine("Strings", culture, "Resources.resw");
            string root = RepositoryPaths.Root();

            return Directory
                .EnumerateFiles(root, "Resources.resw", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                    path.EndsWith(Path.DirectorySeparatorChar + relativePath, StringComparison.OrdinalIgnoreCase)
                    && path.Contains(Path.DirectorySeparatorChar + "EchoPlay.App" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !path.Contains(Path.DirectorySeparatorChar + "EchoPlay.App.Tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Ressource-Datei '{relativePath}' nicht gefunden unterhalb von '{root}'.");
        }
    }
}
