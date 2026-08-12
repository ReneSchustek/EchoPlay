using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;

// Benennt Symbole über eine ganze Projektmappe um - dieselbe Roslyn-Engine, die Visual Studio
// hinter "Symbol umbenennen" verwendet. Referenzen in allen Projekten wandern mit; Kommentare,
// Zeichenketten und Resource-Keys bleiben unberührt (kein Textersatz).
//
// Aufruf:  SymbolRenamer <projektwurzel> <auftrag.json> [--dry-run]
//
// Auftragsdatei:
// {
//   "projects": ["src/ShopAnalytics.Core/ShopAnalytics.Core.csproj", "..."],
//   "renames": [
//     { "kind": "type",     "container": null,             "from": "RobotsRegelwerk", "to": "RobotsRuleSet" },
//     { "kind": "member",   "container": "RobotsRuleSet",  "from": "IstErlaubt",      "to": "IsAllowed" }
//   ]
// }
//
// Lokale Bezeichner laufen nicht über Einzelaufträge, sondern über ein Segment-Wörterbuch:
// {
//   "projects": ["..."],
//   "segmentDictionary": "F:/.../glossar-segmente.txt",
//   "localProjects": ["ShopAnalytics.Core"]
// }
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Aufruf: SymbolRenamer <projektwurzel> <auftrag.json> [--dry-run]");
            return 2;
        }

        string root = Path.GetFullPath(args[0]);
        string jobPath = Path.GetFullPath(args[1]);
        bool onlyShow = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

        RegisterMsBuild();

        Job? job = JsonSerializer.Deserialize<Job>(
            await File.ReadAllTextAsync(jobPath).ConfigureAwait(false),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (job is null || (job.RenameList.Count == 0 && job.SegmentDictionary is null))
        {
            Console.Error.WriteLine("Auftragsdatei enthält weder Umbenennungen noch ein Segment-Wörterbuch.");
            return 2;
        }

        // Eigenschaften aus dem Auftrag überschreiben die Vorgabe. Nötig, wo ein Projekt sich
        // nur mit bestimmten Werten auswerten lässt - ein Testprojekt, das auf eine
        // eigenständige Anwendung verweist, scheitert sonst an NETSDK1150, und Roslyn arbeitet
        // danach ohne aufgelöste Referenzen weiter. Fundstellen fehlen dann still.
        Dictionary<string, string> properties = new(StringComparer.Ordinal) { ["Configuration"] = "Debug" };
        foreach (KeyValuePair<string, string> eigenschaft in job.MsBuildProperties ?? [])
        {
            properties[eigenschaft.Key] = eigenschaft.Value;
        }

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create(properties);
        workspace.LoadMetadataForReferencedProjects = true;
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.Error.WriteLine("  [Workspace] " + e.Diagnostic.Message);
            }
        };

        // Projektmappe vor Einzelprojekten: Manche Projekte lassen sich einzeln gar nicht
        // laden - ein Testprojekt, das auf eine eigenständige Anwendung verweist, scheitert
        // an NETSDK1150. MSBuild meldet das nur als Warnung, Roslyn arbeitet danach ohne
        // aufgelöste Referenzen weiter, und Fundstellen in nicht übersetzbaren Dateien
        // fehlen still. Über die Projektmappe geladen tritt der Fall nicht auf.
        if (job.SolutionPath is not null)
        {
            string solutionPath = Path.GetFullPath(
                Path.Combine(root, job.SolutionPath.Replace('/', Path.DirectorySeparatorChar)));

            Console.WriteLine($"Lade Projektmappe {Path.GetFileName(solutionPath)} ...");
            Solution opened = await workspace.OpenSolutionAsync(solutionPath).ConfigureAwait(false);
            Console.WriteLine($"  {opened.Projects.Count()} Projekt(e).");
        }

        Console.WriteLine($"Lade {job.ProjectList.Count} Projekt(e) ...");
        foreach (string relative in job.ProjectList)
        {
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // OpenProjectAsync zieht Projektreferenzen mit in den Arbeitsbereich. Ein Projekt, das
            // dadurch schon drin ist, darf nicht erneut geöffnet werden - sonst wirft Roslyn.
            if (workspace.CurrentSolution.Projects.Any(p =>
                    string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Project loaded = await workspace.OpenProjectAsync(path).ConfigureAwait(false);
            Console.WriteLine($"  {loaded.Name} ({loaded.Documents.Count()} Dateien)");
        }

        Console.WriteLine($"Arbeitsbereich: {workspace.CurrentSolution.Projects.Count()} Projekt(e).");

        Solution solution = workspace.CurrentSolution;
        int done = 0;
        int skipped = 0;

        Dictionary<string, string>? dictionary = null;
        if (job.SegmentDictionary is not null)
        {
            dictionary = Translator.LoadDictionary(
                Path.GetFullPath(Path.Combine(root, job.SegmentDictionary.Replace('/', Path.DirectorySeparatorChar))));
            Console.WriteLine($"Segment-Wörterbuch: {dictionary.Count} Einträge.");
        }

        List<Rename> allRenames = [.. job.RenameList];
        if (dictionary is not null && job.AutoMembers)
        {
            List<(string Kind, string? Container, string From, string To, string Projekt)> collected = await MemberCollector.CollectAsync(
                solution, dictionary, job.LocalProjects).ConfigureAwait(false);

            Console.WriteLine($"Typen und Member mit deutschem Namen: {collected.Count}");
            allRenames.AddRange(collected.Select(g =>
                new Rename(g.Kind, g.Container, g.From, g.To, [g.Projekt])));
        }

        foreach (Rename rename in allRenames)
        {
            // Denselben Namen kann es mehrfach geben - Test-Doppelgänger wie "FakeZeit" liegen
            // in jedem Testprojekt einzeln. Nach jeder Umbenennung ist die Lösung eine neue,
            // deshalb wird erneut gesucht statt eine Trefferliste vorab einzusammeln.
            int inThisJob = 0;
            while (true)
            {
                ISymbol? symbol = await FindSymbolAsync(solution, rename).ConfigureAwait(false);
                if (symbol is null)
                {
                    break;
                }

                solution = await Renamer.RenameSymbolAsync(
                    solution,
                    symbol,
                    new SymbolRenameOptions(
                        RenameOverloads: true,
                        RenameInStrings: false,
                        RenameInComments: false,
                        RenameFile: false),
                    rename.To).ConfigureAwait(false);

                inThisJob++;
                if (inThisJob >= 50)
                {
                    Console.Error.WriteLine($"  ABBRUCH nach 50 Treffern: {Beschreibe(rename)}");
                    break;
                }
            }

            if (inThisJob == 0)
            {
                Console.Error.WriteLine($"  NICHT GEFUNDEN: {Beschreibe(rename)}");
                skipped++;
                continue;
            }

            done++;
            string multiple = inThisJob > 1 ? $" ({inThisJob}x)" : string.Empty;
            Console.WriteLine($"  ok: {Beschreibe(rename)}{multiple}");
        }

        if (dictionary is not null)
        {
            (Solution afterLocal, int localMatches) = await LocalRenamer.RenameAsync(
                solution, dictionary, job.LocalProjects, onlyShow).ConfigureAwait(false);

            solution = afterLocal;
            done += localMatches;
            Console.WriteLine($"  lokale Bezeichner: {localMatches}");

            if (job.Parameters)
            {
                (Solution afterParam, int paramMatches) = await ParameterRenamer.RenameAsync(
                    solution, dictionary, job.LocalProjects, onlyShow).ConfigureAwait(false);

                solution = afterParam;
                done += paramMatches;
                Console.WriteLine($"  Parameter: {paramMatches}");
            }

            if (job.Members)
            {
                HashSet<string> exceptions = new(job.MemberExceptions ?? [], StringComparer.Ordinal);

                // Die Zuordnung wird vor der Umbenennung geholt: Danach trägt die Projektmappe die
                // neuen Namen, und die alten - die in XAML stehen - wären nicht mehr zu ermitteln.
                Dictionary<string, string> forXaml = await MemberRewriter.NamesAsync(
                    solution, dictionary, job.LocalProjects, exceptions).ConfigureAwait(false);

                (Solution afterMember, int memberMatches) = await MemberRewriter.RenameAsync(
                    solution, dictionary, job.LocalProjects, exceptions, onlyShow).ConfigureAwait(false);

                solution = afterMember;
                done += memberMatches;
                Console.WriteLine($"  Typen und Member: {memberMatches}");

                int xamlFiles = XamlUpdater.Update(root, forXaml, onlyShow);
                Console.WriteLine($"  XAML-Dateien nachgezogen: {xamlFiles}");
            }
        }

        if (onlyShow)
        {
            Console.WriteLine($"Probelauf: {done} Umbenennung(en), {skipped} übersprungen. Nichts geschrieben.");
            return skipped == 0 ? 0 : 1;
        }

        if (!workspace.TryApplyChanges(solution))
        {
            Console.Error.WriteLine("Änderungen konnten nicht geschrieben werden.");
            return 1;
        }

        Console.WriteLine($"Geschrieben: {done} Umbenennung(en), {skipped} übersprungen.");
        return skipped == 0 ? 0 : 1;
    }

    private static string Beschreibe(Rename rename) =>
        rename.Container is null
            ? $"{rename.Kind} {rename.From} -> {rename.To}"
            : $"{rename.Kind} {rename.Container}.{rename.From} -> {rename.To}";

    private static async Task<ISymbol?> FindSymbolAsync(Solution solution, Rename rename)
    {
        foreach (Project project in solution.Projects)
        {
            if (rename.Projects is { Length: > 0 }
                && !rename.Projects.Contains(project.Name, StringComparer.Ordinal))
            {
                continue;
            }

            Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            SymbolFilter filter = rename.Kind == "namespace"
                ? SymbolFilter.Namespace
                : SymbolFilter.TypeAndMember;

            IEnumerable<ISymbol> matches = compilation.GetSymbolsWithName(
                name => string.Equals(name, rename.From, StringComparison.Ordinal),
                filter);

            foreach (ISymbol symbol in matches)
            {
                // Nur Symbole aus dem eigenen Quellcode - Fremdbibliotheken bleiben unberührt.
                if (!symbol.Locations.Any(l => l.IsInSource))
                {
                    continue;
                }

                if (rename.Kind == "type" && symbol is not INamedTypeSymbol)
                {
                    continue;
                }

                if (rename.Kind == "member" && symbol is INamedTypeSymbol)
                {
                    continue;
                }

                // Ein Namensraum-Segment wird nur umbenannt, wenn der volle Pfad passt -
                // "Daten" gibt es in mehreren Modulen, und sie sollen nicht gemeinsam
                // wandern, wenn nur eines gemeint ist.
                if (rename.Kind == "namespace")
                {
                    if (symbol is not INamespaceSymbol space)
                    {
                        continue;
                    }

                    if (rename.Container is not null
                        && !string.Equals(
                            space.ContainingNamespace?.ToDisplayString(),
                            rename.Container,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                else if (rename.Container is not null
                    && !string.Equals(symbol.ContainingType?.Name, rename.Container, StringComparison.Ordinal))
                {
                    continue;
                }

                return symbol;
            }
        }

        return null;
    }

    private static void RegisterMsBuild()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        VisualStudioInstance instanz = MSBuildLocator.QueryVisualStudioInstances()
            .OrderByDescending(i => i.Version)
            .First();

        Console.WriteLine($"MSBuild: {instanz.Name} {instanz.Version} ({instanz.MSBuildPath})");
        MSBuildLocator.RegisterInstance(instanz);
    }

    /// <param name="SolutionPath">
    /// Projektmappe, die vor den Einzelprojekten geladen wird (relativ zur Projektwurzel).
    /// Nötig, wo ein Projekt sich einzeln nicht laden lässt.
    /// </param>
    /// <param name="MsBuildProperties">
    /// Zusätzliche MSBuild-Eigenschaften für das Auswerten der Projekte, etwa Plattform oder
    /// Laufzeitkennung. Überschreiben die Vorgabe.
    /// </param>
    /// <param name="Projects">
    /// Projekte der Projektmappe, die geladen werden. Ohne Angabe: alle.
    /// </param>
    /// <param name="Renames">
    /// Die einzelnen Umbenennungen des Auftrags, in der Reihenfolge ihrer Ausführung.
    /// </param>
    /// <param name="SegmentDictionary">
    /// Pfad zum Segment-Wörterbuch (relativ zur Projektwurzel). Ist er gesetzt, laufen zusätzlich
    /// alle lokalen Bezeichner durch die segmentweise Übersetzung.
    /// </param>
    /// <param name="LocalProjects">
    /// Projekte, deren Bezeichner umbenannt werden. Ohne Angabe: alle geladenen.
    /// </param>
    /// <param name="AutoMembers">
    /// Zusätzlich Typen und Member über das Wörterbuch umbenennen. Deutlich langsamer als der
    /// lokale Durchgang, weil jede Umbenennung die Projektmappe neu übersetzt.
    /// </param>
    /// <param name="Parameters">
    /// Zusätzlich Parameter umbenennen. Zieht benannte Argumente und param-Doku mit.
    /// </param>
    /// <param name="Members">Zusätzlich Typen und Member umbenennen.</param>
    /// <param name="MemberExceptions">
    /// Namen, die trotz deutschem Wortlaut bleiben - Spalten, JSON-Felder, Bindungspfade.
    /// </param>
    private sealed record Job(
        List<string>? Projects,
        List<Rename>? Renames,
        string? SolutionPath = null,
        Dictionary<string, string>? MsBuildProperties = null,
        string? SegmentDictionary = null,
        string[]? LocalProjects = null,
        bool AutoMembers = false,
        bool Parameters = false,
        bool Members = false,
        string[]? MemberExceptions = null)
    {
        public List<string> ProjectList => Projects ?? [];

        public List<Rename> RenameList => Renames ?? [];
    }

    /// <param name="Kind">
    /// Art des Symbols: Typ, Merkmal, Aufzählungswert, Datei. Bestimmt, welcher Umbenenner greift.
    /// </param>
    /// <param name="Container">
    /// Typ, in dem das Symbol steckt. Nur bei Membern nötig, sonst leer.
    /// </param>
    /// <param name="From">Bisheriger Name.</param>
    /// <param name="To">Künftiger Name.</param>
    /// <param name="Projects">
    /// Projekte, in denen das Symbol deklariert ist. Ohne diese Angabe wird jedes Projekt
    /// kompiliert, bis das Symbol auftaucht - bei Membern ist das der teuerste Teil des Laufs,
    /// weil nach jeder Umbenennung alle Compilations neu entstehen. Mehrere Einträge sind
    /// nötig, wenn derselbe Name in mehreren Projekten deklariert wird (Test-Doppelgänger).
    /// </param>
    private sealed record Rename(
        string Kind, string? Container, string From, string To, string[]? Projects = null);
}
