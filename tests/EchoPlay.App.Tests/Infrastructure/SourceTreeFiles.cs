using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Die C#-Dateien des eigenen Quellbaums, über die die Wächter-Tests laufen.
    /// <para>
    /// Ausgenommen sind die Migrationen: Deren <c>.Designer.cs</c> und der Modell-Schnappschuss
    /// entstehen durch <c>dotnet ef</c>. Von Hand geänderter generierter Code würde beim nächsten
    /// Erzeugen überschrieben, eine Regel daran ließe sich also nicht halten.
    /// </para>
    /// </summary>
    internal static class SourceTreeFiles
    {
        private static readonly string[] SourceFolders = ["src", "tests", "tools"];
        private static readonly string[] ExcludedFolderNames = ["bin", "obj", "Migrations"];

        /// <summary>
        /// Liefert alle C#-Dateien unter <c>src</c>, <c>tests</c> und <c>tools</c> ohne Build-Ausgaben
        /// und Migrationen.
        /// </summary>
        /// <returns>Absolute Pfade der Dateien.</returns>
        public static IEnumerable<string> Enumerate()
        {
            string root = RepositoryPaths.Root();
            return SourceFolders
                .Select(folder => Path.Combine(root, folder))
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                .Where(file => !IsExcluded(file, root));
        }

        /// <summary>
        /// Der Pfad relativ zur Wurzel des Repositorys, für lesbare Fundstellen in Meldungen.
        /// </summary>
        /// <param name="file">Absoluter Pfad einer Datei im Repository.</param>
        /// <returns>Der relative Pfad.</returns>
        public static string Relative(string file) => Path.GetRelativePath(RepositoryPaths.Root(), file);

        private static bool IsExcluded(string file, string root)
        {
            string[] segments = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            return segments.Any(segment => ExcludedFolderNames.Contains(segment, StringComparer.Ordinal));
        }
    }
}
