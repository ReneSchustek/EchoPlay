using EchoPlay.App.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace EchoPlay.App.Tests.Views
{
    /// <summary>
    /// Stellt sicher, dass jedes Wörterbuch, das <c>App.xaml</c> lädt, auch tatsächlich
    /// ausgeliefert wird.
    /// <para>
    /// Anlass: Die Wörterbücher unter <c>Themes/</c> sind von der XAML-Übersetzung
    /// ausgenommen und werden als Inhaltsdateien mitkopiert. Diese Liste stand einzeln in der
    /// Projektdatei; ein neu angelegtes Wörterbuch fehlte darin und landete nie im
    /// Ausgabeverzeichnis. <c>App.xaml</c> lud es trotzdem — die Anwendung brach beim Start ab,
    /// noch bevor der Logger stand, also ohne eine einzige Zeile im Protokoll.
    /// </para>
    /// <para>
    /// Weder Übersetzung noch Testlauf haben das bemerkt: Der Pfad wird erst zur Laufzeit
    /// aufgelöst. Diese Prüfung schließt genau die Lücke — sie vergleicht, was geladen wird,
    /// mit dem, was ausgeliefert wird.
    /// </para>
    /// </summary>
    public sealed class ResourceDictionaryPackagingTests
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        [Fact]
        public void EveryDictionaryLoadedByApp_ExistsInTheSourceTree()
        {
            List<string> offenders = [];

            foreach (string relative in DictionarySourcesFromAppXaml())
            {
                string full = Path.Combine(RepositoryPaths.AppProject(), relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    offenders.Add(relative);
                }
            }

            Assert.True(offenders.Count == 0,
                "In App.xaml geladene Wörterbücher, die es gar nicht gibt:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Die eigentliche Zusage: Was geladen wird, muss auch mitkopiert werden. Fehlt der
        /// Eintrag, startet die Anwendung nicht — und zwar ohne Protokoll und ohne Testfehler.
        /// </summary>
        [Fact]
        public void EveryDictionaryLoadedByApp_IsShippedAsContent()
        {
            string projectFile = Path.Combine(RepositoryPaths.AppProject(), "EchoPlay.App.csproj");
            string project = File.ReadAllText(projectFile);

            string[] contentIncludes = [.. Regex
                .Matches(project, @"<Content\s+Include=""(?<path>[^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(5))
                .Select(m => m.Groups["path"].Value)];

            List<string> offenders = [];

            foreach (string relative in DictionarySourcesFromAppXaml())
            {
                if (!contentIncludes.Any(pattern => Covers(pattern, relative)))
                {
                    offenders.Add(relative);
                }
            }

            Assert.True(offenders.Count == 0,
                "Wörterbücher, die App.xaml lädt, die aber nicht als Inhaltsdatei ausgeliefert werden "
                + "(die Anwendung startet damit nicht, ohne eine Zeile im Protokoll):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Prüft, ob ein Eintrag der Projektdatei einen Pfad erfasst — als genaue Angabe oder
        /// als Muster mit <c>*</c>.
        /// </summary>
        /// <param name="pattern">Eintrag aus der Projektdatei, etwa <c>Themes\*.xaml</c>.</param>
        /// <param name="relativePath">Pfad aus App.xaml, etwa <c>Themes/Tokens.xaml</c>.</param>
        /// <returns><see langword="true"/>, wenn der Eintrag den Pfad erfasst.</returns>
        private static bool Covers(string pattern, string relativePath)
        {
            string normalizedPattern = pattern.Replace('\\', '/');
            string normalizedPath = relativePath.Replace('\\', '/');

            if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
            {
                return string.Equals(normalizedPattern, normalizedPath, StringComparison.OrdinalIgnoreCase);
            }

            string asRegex = "^" + Regex.Escape(normalizedPattern).Replace(@"\*", "[^/]*", StringComparison.Ordinal) + "$";
            return Regex.IsMatch(normalizedPath, asRegex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Liest die Quellen aller Wörterbücher, die <c>App.xaml</c> zusammenführt.
        /// </summary>
        /// <returns>Relative Pfade, wie sie in App.xaml stehen.</returns>
        private static IEnumerable<string> DictionarySourcesFromAppXaml()
        {
            string appXaml = Path.Combine(RepositoryPaths.AppProject(), "App.xaml");

            return XDocument.Load(appXaml)
                .Descendants(Xaml + "ResourceDictionary")
                .Select(e => e.Attribute("Source")?.Value)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
