using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Benennt lokale Bezeichner um - lokale Variablen, foreach- und catch-Variablen,
// Pattern-Designations und Query-Variablen.
//
// Warum eigener Weg statt Renamer.RenameSymbolAsync: Ein lokales Symbol wirkt nie über die
// Dokumentgrenze hinaus. Deshalb kann ein Dokument in einem Zug umgeschrieben werden, statt für
// jeden einzelnen Namen die gesamte Lösung neu zu kompilieren. Bei zehntausenden Vorkommen ist
// das der Unterschied zwischen Minuten und Tagen.
//
// Parameter und Felder bleiben bewusst außen vor: Ein Parametername steht auch im benannten
// Argument des Aufrufers und in <param name="...">, beides in anderen Dokumenten. Dafür ist
// Renamer.RenameSymbolAsync zuständig.
internal static class LocalRenamer
{
    public static async Task<(Solution Lösung, int Umbenannt)> RenameAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects,
        bool onlyShow)
    {
        int renamed = 0;

        foreach (ProjectId projectId in solution.ProjectIds.ToList())
        {
            Project project = solution.GetProject(projectId)!;
            if (projects is { Length: > 0 } && !projects.Contains(project.Name, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (DocumentId documentId in project.DocumentIds.ToList())
            {
                Document document = solution.GetDocument(documentId)!;
                (SyntaxNode? newRoot, int inDocument) =
                    await EditDocumentAsync(document, dictionary).ConfigureAwait(false);

                if (inDocument == 0 || newRoot is null)
                {
                    continue;
                }

                renamed += inDocument;
                if (!onlyShow)
                {
                    solution = solution.WithDocumentSyntaxRoot(documentId, newRoot);
                }
            }
        }

        return (solution, renamed);
    }

    private static async Task<(SyntaxNode? Wurzel, int Umbenannt)> EditDocumentAsync(
        Document document,
        IReadOnlyDictionary<string, string> dictionary)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (root is null || model is null)
        {
            return (null, 0);
        }

        Dictionary<ISymbol, string> newNames = CollectSymbolsToRename(root, model, dictionary);
        if (newNames.Count == 0)
        {
            return (null, 0);
        }

        // Ein Zielname, den es im Dokument schon als Bezeichner gibt, wird nicht vergeben. Der
        // Gültigkeitsbereich wäre zwar meist ein anderer, aber eine Verwechslung im selben
        // Dokument ist teurer als ein ausgelassener Name - der fällt beim nächsten Lauf auf.
        //
        // Derselbe Zielname zweimal ist der gefährlichere Fall: "historie" und "verlauf" heißen
        // beide "history". Stehen sie in einer Methode, entsteht CS0128 - oder, schlimmer, zwei
        // Variablen verschmelzen unbemerkt zu einer. Deshalb bekommt jeder Zielname genau einen
        // Träger; der zweite bleibt stehen und fällt beim nächsten Lauf auf.
        HashSet<string> occupied = CollectExistingNames(root);
        foreach (ISymbol symbol in newNames.Keys.ToList())
        {
            string target = newNames[symbol];
            if (!occupied.Add(target))
            {
                _ = newNames.Remove(symbol);
            }
        }

        if (newNames.Count == 0)
        {
            return (null, 0);
        }

        Dictionary<SyntaxToken, string> replacements = CollectTokens(root, model, newNames);
        if (replacements.Count == 0)
        {
            return (null, 0);
        }

        SyntaxNode neueWurzel = root.ReplaceTokens(
            replacements.Keys,
            (alt, _) => SyntaxFactory.Identifier(alt.LeadingTrivia, replacements[alt], alt.TrailingTrivia));

        return (neueWurzel, newNames.Count);
    }

    private static Dictionary<ISymbol, string> CollectSymbolsToRename(
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyDictionary<string, string> dictionary)
    {
        Dictionary<ISymbol, string> matches = new(SymbolEqualityComparer.Default);

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            ISymbol? symbol = node switch
            {
                VariableDeclaratorSyntax v when v.Parent?.Parent is LocalDeclarationStatementSyntax
                    => model.GetDeclaredSymbol(v),
                ForEachStatementSyntax f => model.GetDeclaredSymbol(f),
                CatchDeclarationSyntax c => model.GetDeclaredSymbol(c),
                SingleVariableDesignationSyntax s => model.GetDeclaredSymbol(s),
                QueryContinuationSyntax q => model.GetDeclaredSymbol(q),
                FromClauseSyntax fr => model.GetDeclaredSymbol(fr),
                LetClauseSyntax l => model.GetDeclaredSymbol(l),
                _ => null,
            };

            if (symbol is not ILocalSymbol and not IRangeVariableSymbol)
            {
                continue;
            }

            if (matches.ContainsKey(symbol))
            {
                continue;
            }

            string neu = Translator.Translate(symbol.Name, dictionary);
            if (!string.Equals(neu, symbol.Name, StringComparison.Ordinal))
            {
                matches[symbol] = neu;
            }
        }

        return matches;
    }

    private static HashSet<string> CollectExistingNames(SyntaxNode root)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (SyntaxToken token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                _ = names.Add(token.ValueText);
            }
        }

        return names;
    }

    private static Dictionary<SyntaxToken, string> CollectTokens(
        SyntaxNode root,
        SemanticModel model,
        Dictionary<ISymbol, string> newNames)
    {
        Dictionary<SyntaxToken, string> ersetzungen = new();

        foreach (SyntaxNode knoten in root.DescendantNodes())
        {
            // Verwendungsstellen
            if (knoten is IdentifierNameSyntax usage)
            {
                ISymbol? gebunden = model.GetSymbolInfo(usage).Symbol;
                if (gebunden is not null && newNames.TryGetValue(gebunden, out string? ziel))
                {
                    ersetzungen[usage.Identifier] = ziel;
                }

                continue;
            }

            // Deklarationsstellen
            (SyntaxToken token, ISymbol? symbol) = knoten switch
            {
                VariableDeclaratorSyntax v => (v.Identifier, model.GetDeclaredSymbol(v)),
                ForEachStatementSyntax f => (f.Identifier, model.GetDeclaredSymbol(f)),
                CatchDeclarationSyntax c => (c.Identifier, model.GetDeclaredSymbol(c)),
                SingleVariableDesignationSyntax s => (s.Identifier, model.GetDeclaredSymbol(s)),
                QueryContinuationSyntax q => (q.Identifier, model.GetDeclaredSymbol(q)),
                FromClauseSyntax fr => (fr.Identifier, model.GetDeclaredSymbol(fr)),
                LetClauseSyntax l => (l.Identifier, model.GetDeclaredSymbol(l)),
                _ => (default, null),
            };

            if (symbol is not null && newNames.TryGetValue(symbol, out string? neu))
            {
                ersetzungen[token] = neu;
            }
        }

        return ersetzungen;
    }
}
