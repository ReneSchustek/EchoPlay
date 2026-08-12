using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Benennt Parameter um - Methoden-, Konstruktor-, Lambda- und lokale Funktionsparameter.
//
// Ein Parametername steht an drei Stellen, und zwei davon liegen in fremden Dokumenten:
//   1. Deklaration und Verwendungen im Rumpf   (eigenes Dokument)
//   2. <param name="..."> und <paramref name="..."> (eigenes Dokument, aber XML statt Code)
//   3. benanntes Argument beim Aufrufer         (jedes andere Dokument)
// Im Bestand sind das 6.361 benannte Argumente und 3.204 param-Einträge - keine Randfälle.
//
// Renamer.RenameSymbolAsync könnte das, übersetzt aber für jeden einzelnen Namen die gesamte
// Projektmappe neu. Bei 631 Namen ist das ein Lauf über Stunden. Stattdessen werden erst alle
// Umbenennungen gesammelt und dann in einem Durchgang über alle Dokumente angewandt.
//
// Der Vergleich läuft über einen Schlüssel statt über Symbolgleichheit: Dasselbe Symbol ist in
// der Compilation des deklarierenden Projekts ein anderes Objekt als in der des aufrufenden.
internal static class ParameterRenamer
{
    public static async Task<(Solution Lösung, int Umbenannt)> RenameAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects,
        bool onlyShow)
    {
        Dictionary<string, string> newNames = await CollectAsync(solution, dictionary, projects)
            .ConfigureAwait(false);

        if (newNames.Count == 0 || onlyShow)
        {
            return (solution, newNames.Count);
        }

        // Erst alle Dokumente lesen, dann alle schreiben. Andersherum bindet ein benanntes Argument
        // ins Leere, sobald die zugehörige Deklaration schon umbenannt ist - der alte Name existiert
        // dann nicht mehr, das Symbol ist null, und die Fundstelle bleibt stehen (CS1739).
        Solution originalState = solution;
        Dictionary<DocumentId, SyntaxNode> newRoots = new();

        foreach (ProjectId projectId in originalState.ProjectIds)
        {
            foreach (DocumentId documentId in originalState.GetProject(projectId)!.DocumentIds)
            {
                SyntaxNode? newRoot = await WriteDocumentAsync(
                    originalState.GetDocument(documentId)!, newNames).ConfigureAwait(false);

                if (newRoot is not null)
                {
                    newRoots[documentId] = newRoot;
                }
            }
        }

        foreach (KeyValuePair<DocumentId, SyntaxNode> pair in newRoots)
        {
            solution = solution.WithDocumentSyntaxRoot(pair.Key, pair.Value);
        }

        return (solution, newNames.Count);
    }

    private static async Task<Dictionary<string, string>> CollectAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects)
    {
        Dictionary<string, string> neueNamen = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> occupiedJeMethod = new(StringComparer.Ordinal);

        foreach (ProjectId projektId in solution.ProjectIds)
        {
            Project project = solution.GetProject(projektId)!;
            if (projects is { Length: > 0 } && !projects.Contains(project.Name, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (Document document in project.Documents)
            {
                SyntaxNode? root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
                SemanticModel? model = await document.GetSemanticModelAsync().ConfigureAwait(false);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (ParameterSyntax node in root.DescendantNodes().OfType<ParameterSyntax>())
                {
                    if (model.GetDeclaredSymbol(node) is not IParameterSymbol parameter)
                    {
                        continue;
                    }

                    // Der Parameter eines primären Konstruktors ist bei einem Record zugleich eine
                    // Eigenschaft. Ihn hier umzubenennen ließe jeden ".Muster"-Zugriff stehen -
                    // das ist eine Member-Umbenennung und gehört nicht in diesen Durchgang.
                    if (node.Parent?.Parent is TypeDeclarationSyntax)
                    {
                        continue;
                    }

                    string? key = Key(parameter);
                    if (key is null || neueNamen.ContainsKey(key))
                    {
                        continue;
                    }

                    string neu = Translator.Translate(parameter.Name, dictionary);
                    if (string.Equals(neu, parameter.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IstUeberschreibbar(parameter) && InOtherLanguageReserved(neu))
                    {
                        continue;
                    }

                    // Zwei Parameter derselben Methode dürfen nicht auf denselben Namen fallen:
                    // "historie" und "verlauf" heißen beide "history".
                    string method = parameter.ContainingSymbol.ToDisplayString();
                    if (!occupiedJeMethod.TryGetValue(method, out HashSet<string>? occupied))
                    {
                        occupied = [.. parameter.ContainingSymbol switch
                        {
                            IMethodSymbol m => m.Parameters.Select(p => p.Name),
                            _ => [],
                        }];
                        occupiedJeMethod[method] = occupied;
                    }

                    if (occupied.Add(neu))
                    {
                        neueNamen[key] = neu;
                    }
                }
            }
        }

        return neueNamen;
    }

    private static async Task<SyntaxNode?> WriteDocumentAsync(
        Document document,
        IReadOnlyDictionary<string, string> newNames)
    {
        SyntaxNode? wurzel = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        SemanticModel? modell = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (wurzel is null || modell is null)
        {
            return null;
        }

        Dictionary<SyntaxToken, string> replacements = new();

        foreach (SyntaxNode knoten in wurzel.DescendantNodes(descendIntoTrivia: true))
        {
            switch (knoten)
            {
                // Deklaration
                case ParameterSyntax p when modell.GetDeclaredSymbol(p) is IParameterSymbol ps:
                    Merke(replacements, p.Identifier, ps, newNames);
                    break;

                // Verwendung im Rumpf
                case IdentifierNameSyntax v
                    when modell.GetSymbolInfo(v).Symbol is IParameterSymbol vs:
                    Merke(replacements, v.Identifier, vs, newNames);
                    break;

                // Benanntes Argument beim Aufrufer: Methode(wert: 3)
                case NameColonSyntax n
                    when modell.GetSymbolInfo(n.Name).Symbol is IParameterSymbol ns:
                    Merke(replacements, n.Name.Identifier, ns, newNames);
                    break;
            }
        }

        CollectDocs(wurzel, modell, newNames, replacements);

        Dictionary<SyntaxToken, string> templates = CollectTemplates(wurzel, modell, newNames);

        if (replacements.Count == 0 && templates.Count == 0)
        {
            return null;
        }

        // Bezeichner und Vorlagen wandern in einem Zug. Zwei aufeinanderfolgende Durchgänge gingen
        // nicht: Nach dem ersten ist der Baum ein anderer, und die Token des zweiten liegen darin
        // nicht mehr.
        return wurzel.ReplaceTokens(
            replacements.Keys.Concat(templates.Keys),
            (alt, _) => replacements.TryGetValue(alt, out string? identifier)
                ? SyntaxFactory.Identifier(alt.LeadingTrivia, identifier, alt.TrailingTrivia)
                : SyntaxFactory.Literal(
                    alt.LeadingTrivia, $"\"{templates[alt]}\"", templates[alt], alt.TrailingTrivia));
    }

    // Ein Log-Platzhalter bindet an den Parameternamen, steht aber als Zeichenkette im
    // [LoggerMessage]-Attribut: Message = "... {Tot} ...". Wandert der Parameter allein, bricht der
    // Quell-Generator mit LOGGEN010 ab - im Bestand betrifft das 110 Vorlagen.
    private static Dictionary<SyntaxToken, string> CollectTemplates(
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyDictionary<string, string> newNames)
    {
        Dictionary<SyntaxToken, string> vorlagen = new();

        foreach (MethodDeclarationSyntax methode in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(methode) is not IMethodSymbol symbol)
            {
                continue;
            }

            Dictionary<string, string> placeholder = [];
            foreach (IParameterSymbol parameter in symbol.Parameters)
            {
                string? parameterKey = Key(parameter);
                if (parameterKey is not null && newNames.TryGetValue(parameterKey, out string? neu))
                {
                    placeholder[parameter.Name] = neu;
                }
            }

            if (placeholder.Count == 0)
            {
                continue;
            }

            foreach (LiteralExpressionSyntax literal in methode.AttributeLists
                .SelectMany(list => list.DescendantNodes())
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                string alt = literal.Token.ValueText;
                string neu = ReplacePlaceholders(alt, placeholder);
                if (!string.Equals(alt, neu, StringComparison.Ordinal))
                {
                    vorlagen[literal.Token] = neu;
                }
            }

            CollectRoutes(root, symbol, placeholder, vorlagen);
        }

        return vorlagen;
    }

    // Eine Minimal-API bindet den Routenabschnitt über den Namen an den Methodenparameter:
    // MapGet("/jobs/{jobId:int}", GetJobAsync). Die Route steht als Zeichenkette an einer ganz
    // anderen Stelle als die Methode. Passen die Namen nicht zusammen, bleibt der Build grün und der
    // Endpunkt antwortet zur Laufzeit mit 400 statt mit dem Fachergebnis.
    private static void CollectRoutes(
        SyntaxNode root,
        IMethodSymbol symbol,
        IReadOnlyDictionary<string, string> placeholder,
        Dictionary<SyntaxToken, string> templates)
    {
        foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax access
                || !access.Name.Identifier.ValueText.StartsWith("Map", StringComparison.Ordinal))
            {
                continue;
            }

            // Der zweite Parameter nennt die Methode, deren Signatur die Namen vorgibt.
            bool matches = call.ArgumentList.Arguments
                .Any(a => a.Expression is IdentifierNameSyntax id
                    && string.Equals(id.Identifier.ValueText, symbol.Name, StringComparison.Ordinal));

            if (!matches)
            {
                continue;
            }

            foreach (LiteralExpressionSyntax literal in call.ArgumentList.Arguments
                .Select(a => a.Expression)
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                string alt = literal.Token.ValueText;
                string neu = ReplaceRoutePlaceholders(alt, placeholder);
                if (!string.Equals(alt, neu, StringComparison.Ordinal))
                {
                    templates[literal.Token] = neu;
                }
            }
        }
    }

    // In der Route steht der Name kleingeschrieben und trägt oft eine Einschränkung: {jobId:int}.
    private static string ReplaceRoutePlaceholders(string route, IReadOnlyDictionary<string, string> placeholder)
        => System.Text.RegularExpressions.Regex.Replace(
            route,
            @"\{([A-Za-zÄÖÜäöüß_][A-Za-z0-9ÄÖÜäöüß_]*)(:[^}]*)?\}",
            matches => placeholder.TryGetValue(matches.Groups[1].Value, out string? neu)
                ? $"{{{neu}{matches.Groups[2].Value}}}"
                : matches.Value);

    private static string ReplacePlaceholders(string template, IReadOnlyDictionary<string, string> placeholder)
        => System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{([A-Za-zÄÖÜäöüß_][A-Za-z0-9ÄÖÜäöüß_]*)([:,][^}]*)?\}",
            matches =>
            {
                foreach (KeyValuePair<string, string> paar in placeholder)
                {
                    // Der Quell-Generator vergleicht Platzhalter und Parametername ohne Rücksicht
                    // auf Groß- und Kleinschreibung; die Vorlage schreibt ihn groß.
                    if (string.Equals(matches.Groups[1].Value, paar.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        string replacement = char.ToUpperInvariant(paar.Value[0]) + paar.Value[1..];
                        return $"{{{replacement}{matches.Groups[2].Value}}}";
                    }
                }

                return matches.Value;
            });


    // <param name="X"> und <paramref name="X"> stehen als XML-Attribut in der Trivia. Sie meinen
    // denselben Parameter, tragen den Namen aber als Zeichenkette - ohne diesen Schritt meldet der
    // Compiler CS1572/CS1573.
    //
    // Der Parameter wird über den Member aufgelöst, zu dessen Doku das Attribut gehört, nicht
    // über das Binden des XML-Knotens: Ein Attributknoten lässt sich nur im unveränderten Baum
    // binden, und der ist nach der ersten Ersetzung weg.
    private static void CollectDocs(
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyDictionary<string, string> newNames,
        Dictionary<SyntaxToken, string> replacements)
    {
        foreach (XmlNameAttributeSyntax attribute in root
            .DescendantNodes(descendIntoTrivia: true)
            .OfType<XmlNameAttributeSyntax>())
        {
            SyntaxNode? ownerNode = attribute
                .Ancestors(ascendOutOfTrivia: true)
                .FirstOrDefault(a => a is MemberDeclarationSyntax or LocalFunctionStatementSyntax);

            if (ownerNode is null || model.GetDeclaredSymbol(ownerNode) is not ISymbol owner)
            {
                continue;
            }

            string wanted = attribute.Identifier.Identifier.ValueText;
            foreach (IParameterSymbol p in ParametersOf(owner))
            {
                if (string.Equals(p.Name, wanted, StringComparison.Ordinal))
                {
                    Merke(replacements, attribute.Identifier.Identifier, p, newNames);
                    break;
                }
            }
        }
    }

    // Ein <param> hängt an einer Methode, an einem Delegaten - oder an einem Record, dessen
    // Parameter am primären Konstruktor hängen.
    private static IEnumerable<IParameterSymbol> ParametersOf(ISymbol owner) => owner switch
    {
        IMethodSymbol methode => methode.Parameters,
        INamedTypeSymbol type => type.InstanceConstructors.SelectMany(k => k.Parameters),
        _ => [],
    };

    private static void Merke(
        Dictionary<SyntaxToken, string> replacements,
        SyntaxToken token,
        IParameterSymbol parameter,
        IReadOnlyDictionary<string, string> newNames)
    {
        string? parameterKey = Key(parameter);
        if (parameterKey is not null && newNames.TryGetValue(parameterKey, out string? neu))
        {
            replacements[token] = neu;
        }
    }

    // Eindeutig über Compilationsgrenzen hinweg: die Doku-Kennung der tragenden Methode plus die
    // Stelle in der Parameterliste. Über Symbolgleichheit ginge das nicht - dasselbe Symbol ist
    // im aufrufenden Projekt ein anderes Objekt.
    //
    // Maßgeblich ist dabei nicht die Methode selbst, sondern ihre Wurzel: Trägt eine Umsetzung
    // einen anderen Parameternamen als die Schnittstelle, meldet CA1725. Über den gemeinsamen
    // Schlüssel bekommt die ganze Familie denselben neuen Namen.
    private static string? Key(IParameterSymbol parameter)
    {
        parameter = OnDeclaration(parameter);

        ISymbol? owner = parameter.ContainingSymbol is IMethodSymbol methode
            ? Root(methode)
            : parameter.ContainingSymbol;

        if (owner is null)
        {
            return null;
        }

        string? kennung = owner.GetDocumentationCommentId();
        if (string.IsNullOrEmpty(kennung))
        {
            // Lambdas und lokale Funktionen haben keine Doku-Kennung. Sie sind dokumentlokal,
            // deshalb reicht die Deklarationsstelle als Kennzeichen.
            Location? location = owner.Locations.FirstOrDefault(l => l.IsInSource);
            if (location is null)
            {
                return null;
            }

            kennung = $"{location.SourceTree?.FilePath}:{location.SourceSpan.Start}";
        }

        return $"{kennung}#{parameter.Ordinal}";
    }

    // An der Aufrufstelle steht selten dasselbe Symbol wie an der Deklaration:
    //
    //   services.AddSchedulerCore(loopActive: true)  - die reduzierte Form der Erweiterungsmethode
    //                                                  hat den this-Parameter nicht, alle Ordinals
    //                                                  sind um eins verschoben
    //   LifecycleContext.Create(handler, options: x)  - die konstruierte generische Methode trägt
    //                                                  eine andere Doku-Kennung als ihre Definition
    //
    // Beide Male zeigt der Schlüssel sonst ins Leere und das benannte Argument bleibt stehen.
    private static IParameterSymbol OnDeclaration(IParameterSymbol parameter)
    {
        if (parameter.ContainingSymbol is not IMethodSymbol methode)
        {
            return parameter;
        }

        if (methode.ReducedFrom is { } unreduziert)
        {
            methode = unreduziert;
            return methode.Parameters[parameter.Ordinal + 1];
        }

        return SymbolEqualityComparer.Default.Equals(methode, methode.OriginalDefinition)
            ? parameter
            : methode.OriginalDefinition.Parameters[parameter.Ordinal];
    }

    // CA1716 verbietet an überschreibbaren Membern Parameternamen, die in einer anderen
    // .NET-Sprache Schlüsselwort sind - wer die Methode in VB umsetzt, könnte sie sonst nicht
    // schreiben. An gewöhnlichen Methoden sind dieselben Namen erlaubt und oft die besten, deshalb
    // greift die Sperre nur dort, wo die Regel sie fordert.
    private static readonly HashSet<string> OtherLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "alias", "as", "class", "date", "each", "end", "error", "event", "friend", "from",
        "function", "handle", "imports", "inherits", "integer", "let", "loop", "me", "mod",
        "module", "my", "next", "nothing", "object", "of", "on", "option", "overloads",
        "property", "raiseevent", "redim", "rem", "resume", "select", "shared", "single",
        "step", "stop", "structure", "sub", "then", "to", "until", "wend", "xor",
    };

    private static bool InOtherLanguageReserved(string name) => OtherLanguages.Contains(name);

    private static bool IstUeberschreibbar(IParameterSymbol parameter)
    {
        // Maßgeblich ist die Wurzel der Familie: Wird die Umsetzung zuerst gefunden, ist sie selbst
        // nicht virtuell - die Schnittstelle darüber aber schon, und die Regel gilt für beide.
        ISymbol? owner = parameter.ContainingSymbol is IMethodSymbol methode
            ? Root(methode)
            : parameter.ContainingSymbol;

        return owner is not null
            && (owner.IsVirtual
                || owner.IsAbstract
                || owner.IsOverride
                || owner.ContainingType?.TypeKind == TypeKind.Interface);
    }

    // Die Stelle, an der der Parametername ursprünglich festgelegt wurde: die Schnittstelle, sonst
    // die oberste überschriebene Methode, sonst die Methode selbst.
    private static IMethodSymbol Root(IMethodSymbol method)
    {
        while (method.OverriddenMethod is not null)
        {
            method = method.OverriddenMethod;
        }

        foreach (INamedTypeSymbol schnittstelle in method.ContainingType.AllInterfaces)
        {
            foreach (IMethodSymbol open in schnittstelle.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(
                        method.ContainingType.FindImplementationForInterfaceMember(open), method))
                {
                    return open;
                }
            }
        }

        return method;
    }
}
