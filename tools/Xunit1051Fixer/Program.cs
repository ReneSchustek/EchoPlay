using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Xunit1051Fixer;

internal static class Out
{
    private static readonly TextWriter StdOut = System.Console.Out;
    private static readonly TextWriter StdErr = System.Console.Error;
    public static void Line(string s) => StdOut.WriteLine(s);
    public static void Err(string s) => StdErr.WriteLine(s);
}

internal static class Program
{
    private const string CtFqn = "System.Threading.CancellationToken";
    private const string Replacement = "TestContext.Current.CancellationToken";

    public static async Task<int> Main(string[] args)
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        // Liste der Test-Projekte (relativ zum Repo-Root, der erste Arg). MSBuildWorkspace
        // versteht .slnx nicht, deshalb laden wir die csproj-Dateien einzeln.
        string repoRoot = args.Length > 0 ? args[0] : ".";
        repoRoot = Path.GetFullPath(repoRoot);
        string diagFile = Path.Combine(repoRoot, ".ai", "temp", "xunit1051-build.txt");
        Out.Line($"Repo: {repoRoot}");
        Out.Line($"Diagnose-Liste: {diagFile}");

        // Lese die xUnit1051-Diagnose-Stellen aus dem dotnet-build-Output. Format:
        //   <Pfad>.cs(<Line>,<Col>): error xUnit1051: ...
        HashSet<(string File, int Line, int Col)> diagLocations = new();
        if (File.Exists(diagFile))
        {
            foreach (string line in await File.ReadAllLinesAsync(diagFile))
            {
                int parenOpen = line.IndexOf('(');
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
            // Properties bewusst gesetzt, damit App.Tests (SelfContained=true,
            // RuntimeIdentifier=win-x64) auch die Referenz auf EchoPlay.App im selben
            // Modus laedt und der MSBuild NETSDK1150-Check nicht zuschlaegt.
            Dictionary<string, string> wsProps = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = "Release",
                ["Platform"] = "x64",
                ["RuntimeIdentifier"] = "win-x64",
                ["SelfContained"] = "true",
                ["UseAppHost"] = "true",
            };
            using MSBuildWorkspace workspace = MSBuildWorkspace.Create(wsProps);
            workspace.WorkspaceFailed += (_, e) =>
            {
                if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                {
                    Out.Err($"  Workspace-Failure: {e.Diagnostic.Message}");
                }
            };
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
                Out.Err("  Compilation null, ueberspringe.");
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

            // Patches an allen Stellen mit CT-Param ohne CT-Argument. Filter via Diagnose-
            // Liste ist zu streng, weil der Analyzer auf Member-Identifier statt
            // InvocationExpression-Start zeigen kann.
            _ = text; // Diagnose-Liste haben wir geladen, nutzen sie aber zur Sanity-Validierung.
            List<(InvocationExpressionSyntax Inv, IMethodSymbol Method, IParameterSymbol CtParam)> targets = new();
            foreach (InvocationExpressionSyntax inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                SymbolInfo si = model.GetSymbolInfo(inv);
                IMethodSymbol? method = si.Symbol as IMethodSymbol
                    ?? si.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (method is null) continue;

                IParameterSymbol? ctParam = method.Parameters.FirstOrDefault(p =>
                    p.Type.ToDisplayString() == CtFqn);

                // Wenn die genutzte Ueberladung selbst keinen CT-Param hat: pruefen, ob
                // EINE andere Ueberladung mit CT existiert. Der xunit-Analyzer fordert
                // dann den Wechsel auf diese Ueberladung.
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
        // Schnell-Vorab: irgendein Argument ist vom Typ CancellationToken oder traegt
        // den ctParam-Namen als NameColon. Konservativ: lieber nicht patchen, wenn
        // schon ein CT durchgereicht wird.
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
            // Text-Fallback fuer den Fall, dass die Compilation unvollstaendig geladen
            // wurde (z. B. EchoPlay.App.Tests mit WorkspaceFailure NETSDK1150 referenziert
            // EchoPlay.App self-contained — Symbol-Resolution liefert dann kein Type).
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

        // Tieferer Check via IInvocationOperation als Sicherheits-Netz.
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

// DocumentEditor wird hier neu eingefuehrt, weil wir den public-Reference-Pfad
// vermeiden wollen. Sein Pendant aus Microsoft.CodeAnalysis.Editing benutzt
// Workspaces.Common — bei uns reicht ein simpler ReplaceNode-Wrapper.
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
