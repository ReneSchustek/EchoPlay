using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Xunit1051Fixer;

/// <summary>
/// Schreibt Meldungen auf die Standardausgabe und die Fehlerausgabe.
/// </summary>
internal static class Out
{
    private static readonly TextWriter StdOut = System.Console.Out;
    private static readonly TextWriter StdErr = System.Console.Error;
    public static void Line(string s) => StdOut.WriteLine(s);
    public static void Err(string s) => StdErr.WriteLine(s);
}

/// <summary>
/// Behebt die Meldung xUnit1051 in den Testprojekten: Wo ein Aufruf ohne CancellationToken läuft,
/// obwohl die Methode einen annimmt, wird TestContext.Current.CancellationToken übergeben.
/// <para>
/// Geändert wird jede Stelle, die dieses Muster trifft. Die gespeicherte Build-Ausgabe mit den
/// Meldungen wird nur geladen und gezählt. Die Projekte werden einzeln geladen, weil
/// MSBuildWorkspace keine .slnx-Dateien liest.
/// </para>
/// </summary>
internal static class Program
{
    private const string CtFqn = "System.Threading.CancellationToken";
    private const string Replacement = "TestContext.Current.CancellationToken";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Ein Projekt, das MSBuildWorkspace nicht laden kann, wird gemeldet und übersprungen; die übrigen Projekte laufen weiter.")]
    public static async Task<int> Main(string[] args)
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        // Die Testprojekte, relativ zum Ordner aus dem ersten Argument. MSBuildWorkspace
        // liest keine .slnx, deshalb werden die csproj-Dateien einzeln geladen.
        string repoRoot = args.Length > 0 ? args[0] : ".";
        repoRoot = Path.GetFullPath(repoRoot);
        // Die gespeicherte Build-Ausgabe kommt aus dem zweiten Argument, sonst liegt sie im Ordner selbst.
        string diagFile = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(repoRoot, "xunit1051-build.txt");
        Out.Line($"Repo: {repoRoot}");
        Out.Line($"Diagnose-Liste: {diagFile}");

        // Die xUnit1051-Fundstellen aus der gespeicherten Build-Ausgabe. Format:
        //   <Pfad>.cs(<Line>,<Col>): error xUnit1051: ...
        HashSet<(string File, int Line, int Col)> diagLocations = new();
        if (File.Exists(diagFile))
        {
            foreach (string line in await File.ReadAllLinesAsync(diagFile))
            {
                int parenOpen = line.IndexOf('(', StringComparison.Ordinal);
                int parenClose = line.IndexOf(')', parenOpen + 1);
                if (parenOpen <= 0 || parenClose < 0) continue;
                string path = line.Substring(0, parenOpen);
                string[] lc = line.Substring(parenOpen + 1, parenClose - parenOpen - 1).Split(',');
                if (lc.Length != 2) continue;
                if (!int.TryParse(lc[0], out int ln) || !int.TryParse(lc[1], out int col)) continue;
                diagLocations.Add((Path.GetFullPath(path), ln, col));
            }
        }
        Out.Line($"Diagnose-Stellen geladen: {diagLocations.Count}");

        string[] testProjects =
        {
            "EchoPlay.App.Tests/EchoPlay.App.Tests.csproj",
            "EchoPlay.AppleMusic.Tests/EchoPlay.AppleMusic.Tests.csproj",
            "EchoPlay.Core.Tests/EchoPlay.Core.Tests.csproj",
            "EchoPlay.Data.Tests/EchoPlay.Data.Tests.csproj",
            "EchoPlay.Fuzz/EchoPlay.Fuzz.csproj",
            "EchoPlay.LocalLibrary.Tests/EchoPlay.LocalLibrary.Tests.csproj",
            "EchoPlay.Logger.Tests/EchoPlay.Logger.Tests.csproj",
            "EchoPlay.Spotify.Tests/EchoPlay.Spotify.Tests.csproj",
            "EchoPlay.TagManager.Tests/EchoPlay.TagManager.Tests.csproj",
        };

        int totalPatches = 0;
        int totalProjects = 0;

        foreach (string rel in testProjects)
        {
            string csproj = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(csproj))
            {
                Out.Err($"FEHLT: {csproj}");
                continue;
            }
            Out.Line($"\n=== {Path.GetFileNameWithoutExtension(csproj)} ===");
            // Die Eigenschaften sorgen dafür, dass App.Tests (SelfContained=true,
            // RuntimeIdentifier=win-x64) die Referenz auf EchoPlay.App im selben Modus lädt;
            // sonst bricht MSBuild mit NETSDK1150 ab.
            Dictionary<string, string> wsProps = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = "Release",
                ["Platform"] = "x64",
                ["RuntimeIdentifier"] = "win-x64",
                ["SelfContained"] = "true",
                ["UseAppHost"] = "true",
            };
            using MSBuildWorkspace workspace = MSBuildWorkspace.Create(wsProps);
            using IDisposable workspaceFailedRegistration = workspace.RegisterWorkspaceFailedHandler(e =>
            {
                if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                {
                    Out.Err($"  Workspace-Failure: {e.Diagnostic.Message}");
                }
            });
            Project project;
            try
            {
                project = await workspace.OpenProjectAsync(csproj);
            }
            catch (Exception ex)
            {
                Out.Err($"  Lade-Fehler: {ex.Message}");
                continue;
            }
            totalProjects++;

            Compilation? compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                Out.Err("  Compilation null, überspringe.");
                continue;
            }
            int patches = await FixProjectAsync(project, compilation, diagLocations);
            totalPatches += patches;
            Out.Line($"  Patches: {patches}");
        }

        Out.Line($"\nGesamt: {totalProjects} Projekte, {totalPatches} Patches.");
        return 0;
    }

    private static async Task<int> FixProjectAsync(Project project, Compilation compilation, HashSet<(string File, int Line, int Col)> diagLocations)
    {
        int patches = 0;
        foreach (Document doc in project.Documents)
        {
            if (doc.FilePath is null || !doc.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string fullPath = Path.GetFullPath(doc.FilePath);
            SyntaxTree? tree = await doc.GetSyntaxTreeAsync();
            SemanticModel? model = compilation.GetSemanticModel(tree!);
            if (tree is null || model is null) continue;

            SyntaxNode root = await tree.GetRootAsync();
            SourceText text = await tree.GetTextAsync();

            // Geändert wird jeder Aufruf, dessen Methode ein CancellationToken annimmt und keines
            // bekommt. Ein Abgleich mit der Fundstellenliste wäre zu streng: Der Analyzer zeigt
            // teils auf den Membernamen statt auf den Anfang des Aufrufs.
            _ = text; // Der Text wird nicht gebraucht, die Diagnose-Liste ebenso wenig.
            List<(InvocationExpressionSyntax Inv, IMethodSymbol Method, IParameterSymbol CtParam)> targets = new();
            foreach (InvocationExpressionSyntax inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                SymbolInfo si = model.GetSymbolInfo(inv);
                IMethodSymbol? method = si.Symbol as IMethodSymbol
                    ?? si.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (method is null) continue;

                IParameterSymbol? ctParam = method.Parameters.FirstOrDefault(p =>
                    p.Type.ToDisplayString() == CtFqn);

                // Hat die gewählte Überladung kein CancellationToken, wird geprüft, ob es genau
                // eine andere mit Token gibt. Der Analyzer verlangt dann den Wechsel auf sie.
                if (ctParam is null)
                {
                    IMethodSymbol? overload = FindCtOverload(method);
                    if (overload is null) continue;
                    ctParam = overload.Parameters.First(p => p.Type.ToDisplayString() == CtFqn);
                    method = overload;
                }

                if (HasCtArgument(inv, ctParam, model)) continue;

                targets.Add((inv, method, ctParam));
            }
            _ = fullPath;
            _ = diagLocations;

            if (targets.Count == 0) continue;

            DocumentEditor editor = await DocumentEditor.CreateAsync(doc);
            foreach ((InvocationExpressionSyntax inv, IMethodSymbol method, IParameterSymbol ctParam) in targets)
            {
                ArgumentListSyntax newArgs = BuildNewArgumentList(inv.ArgumentList, ctParam);
                InvocationExpressionSyntax newInv = inv.WithArgumentList(newArgs);
                editor.ReplaceNode(inv, newInv);
            }

            Document newDoc = editor.GetChangedDocument();
            SyntaxNode? newRoot = await newDoc.GetSyntaxRootAsync();
            if (newRoot is null) continue;
            string newText = newRoot.ToFullString();
            await File.WriteAllTextAsync(doc.FilePath!, newText);
            patches += targets.Count;
        }
        return patches;
    }

    private static IMethodSymbol? FindCtOverload(IMethodSymbol method)
    {
        if (method.ContainingType is not INamedTypeSymbol container) return null;
        foreach (IMethodSymbol candidate in EnumerateMethodOverloads(container, method.Name))
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, method)) continue;
            IParameterSymbol? ct = candidate.Parameters.FirstOrDefault(p => p.Type.ToDisplayString() == CtFqn);
            if (ct is null) continue;
            if (candidate.Parameters.Length < method.Parameters.Length) continue;
            return candidate;
        }
        return null;
    }

    private static IEnumerable<IMethodSymbol> EnumerateMethodOverloads(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            foreach (ISymbol m in t.GetMembers(name))
            {
                if (m is IMethodSymbol ms) yield return ms;
            }
        }
    }

    private static bool HasCtArgument(InvocationExpressionSyntax inv, IParameterSymbol ctParam, SemanticModel model)
    {
        // Vorprüfung: Ist ein Argument schon ein CancellationToken oder als benanntes Argument
        // an den Token-Parameter gebunden, bleibt der Aufruf unverändert. Im Zweifel lieber
        // nichts ändern.
        foreach (ArgumentSyntax arg in inv.ArgumentList.Arguments)
        {
            if (arg.NameColon is { Name.Identifier.ValueText: string nm } && nm == ctParam.Name)
            {
                return true;
            }
            TypeInfo ti = model.GetTypeInfo(arg.Expression);
            if (ti.Type?.ToDisplayString() == CtFqn || ti.ConvertedType?.ToDisplayString() == CtFqn)
            {
                return true;
            }
            // Rückfall auf den Text, falls die Compilation unvollständig geladen ist: Scheitert
            // die Referenz von EchoPlay.App.Tests auf EchoPlay.App an NETSDK1150, liefert die
            // Symbolauflösung keinen Typ.
            string exprText = arg.Expression.ToString();
            if (exprText == "CancellationToken.None"
                || exprText.EndsWith(".CancellationToken", StringComparison.Ordinal)
                || exprText.EndsWith(".Token", StringComparison.Ordinal)
                || exprText == "ct"
                || exprText == "cancellationToken"
                || exprText == "token")
            {
                return true;
            }
        }

        // Zur Sicherheit zusätzlich über IInvocationOperation prüfen.
        IOperation? op = model.GetOperation(inv);
        if (op is IInvocationOperation invOp)
        {
            foreach (IArgumentOperation arg in invOp.Arguments)
            {
                if (arg.Parameter is null) continue;
                bool sameParam = SymbolEqualityComparer.Default.Equals(arg.Parameter, ctParam)
                    || (arg.Parameter.Name == ctParam.Name
                        && SymbolEqualityComparer.Default.Equals(arg.Parameter.ContainingSymbol, ctParam.ContainingSymbol));
                if (sameParam)
                {
                    return arg.ArgumentKind != ArgumentKind.DefaultValue;
                }
            }
        }
        return false;
    }

    private static ArgumentListSyntax BuildNewArgumentList(ArgumentListSyntax orig, IParameterSymbol ctParam)
    {
        ArgumentSyntax ctArg = SyntaxFactory.Argument(
            SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(ctParam.Name)),
            SyntaxFactory.Token(SyntaxKind.None),
            SyntaxFactory.ParseExpression(Replacement));

        return orig.AddArguments(ctArg);
    }
}

/// <summary>
/// Sammelt Knotenersetzungen eines Dokuments und wendet sie in einem Schritt an. Ein schlanker
/// Ersatz für DocumentEditor aus Microsoft.CodeAnalysis.Editing, der nur ReplaceNode kennt.
/// </summary>
internal sealed class DocumentEditor
{
    private readonly Document _document;
    private SyntaxNode _root;
    private readonly Dictionary<SyntaxNode, SyntaxNode> _replacements = new();

    private DocumentEditor(Document document, SyntaxNode root)
    {
        _document = document;
        _root = root;
    }

    public static async Task<DocumentEditor> CreateAsync(Document document)
    {
        SyntaxNode? r = await document.GetSyntaxRootAsync() ?? throw new InvalidOperationException("Kein SyntaxRoot");
        return new DocumentEditor(document, r);
    }

    public void ReplaceNode(SyntaxNode original, SyntaxNode replacement)
    {
        _replacements[original] = replacement;
    }

    public Document GetChangedDocument()
    {
        if (_replacements.Count == 0) return _document;
        SyntaxNode newRoot = _root.ReplaceNodes(_replacements.Keys, (orig, _) => _replacements[orig]);
        return _document.WithSyntaxRoot(newRoot);
    }
}
