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

        // Ein Zielname, den es im Gültigkeitsbereich schon gibt, wird nicht vergeben.
        //
        // Derselbe Zielname zweimal ist der gefährliche Fall: "historie" und "verlauf" heißen
        // beide "history". Stehen sie in einer Methode, entsteht CS0128 - oder, schlimmer, zwei
        // Variablen verschmelzen unbemerkt zu einer. Deshalb bekommt jeder Zielname innerhalb
        // eines Members genau einen Träger; der zweite bleibt stehen.
        //
        // Der Bereich ist der umschließende Member, nicht das Dokument: In einer Testklasse mit
        // vierzig Methoden hieße sonst schon die zweite Variable wie eine aus einer fremden
        // Methode, und beide blieben für immer deutsch. Dazu kommen die Felder und Eigenschaften
        // des Typs - eine lokale Variable darf ein Feld zwar verdecken, aber dann trifft jede
        // Verwendung des Feldnamens im selben Member plötzlich die Variable, und das übersetzt
        // sich fehlerfrei.
        HashSet<string> fields = CollectFieldNames(root);
        Dictionary<SyntaxNode, HashSet<string>> occupiedPerMember = new();
        foreach (ISymbol symbol in newNames.Keys.ToList())
        {
            SyntaxNode? scope = ScopeOf(symbol, root);
            if (scope is null)
            {
                _ = newNames.Remove(symbol);
                continue;
            }

            if (!occupiedPerMember.TryGetValue(scope, out HashSet<string>? occupied))
            {
                occupied = [.. CollectExistingNames(scope), .. fields];
                occupiedPerMember[scope] = occupied;
            }

            if (!occupied.Add(newNames[symbol]))
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

    // Der Gültigkeitsbereich einer lokalen Variable endet an der Memberdeklaration. Ein Feld-
    // oder Eigenschafts-Initialisierer trägt ebenfalls Anweisungen, deshalb genügt der Aufstieg
    // bis zum ersten Member; eine lokale Funktion ist Teil ihres Members und bekommt keinen
    // eigenen Bereich.
    private static SyntaxNode? ScopeOf(ISymbol symbol, SyntaxNode root)
    {
        SyntaxReference? reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null || reference.SyntaxTree != root.SyntaxTree)
        {
            return null;
        }

        return reference.GetSyntax().FirstAncestorOrSelf<MemberDeclarationSyntax>();
    }

    private static HashSet<string> CollectFieldNames(SyntaxNode root)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            switch (node)
            {
                case FieldDeclarationSyntax field:
                    foreach (VariableDeclaratorSyntax declarator in field.Declaration.Variables)
                    {
                        _ = names.Add(declarator.Identifier.ValueText);
                    }

                    break;
                case PropertyDeclarationSyntax property:
                    _ = names.Add(property.Identifier.ValueText);
                    break;
                default:
                    break;
            }
        }

        return names;
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
