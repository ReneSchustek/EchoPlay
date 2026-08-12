using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Benennt Typen und Member um - Klassen, Schnittstellen, Enums, Methoden, Eigenschaften, Felder,
// Ereignisse und Enum-Werte.
//
// Anders als bei lokalen Bezeichnern ist hier jede Verwendungsstelle im ganzen Bestand betroffen,
// und anders als bei Parametern gibt es keine feste Stellenzahl: Der Name steht in
// Objektinitialisierern, in nameof, in <see cref="..."/> und in Attributargumenten.
//
// Nicht angefasst werden:
//   - überschriebene Member und Schnittstellen-Umsetzungen (sie erben den Namen über die Wurzel)
//   - Member, deren Name aus einer fremden Bibliothek stammt
//   - Eigenschaften, deren Name eine Spalte, ein JSON-Feld oder einen Bindungspfad trägt - die
//     stehen im Auftrag als Ausnahme
internal static class MemberRewriter
{
    public static async Task<(Solution Lösung, int Umbenannt)> RenameAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects,
        IReadOnlySet<string> exceptions,
        bool onlyShow)
    {
        Dictionary<string, string> newNames = await CollectAsync(solution, dictionary, projects, exceptions)
            .ConfigureAwait(false);

        if (newNames.Count == 0 || onlyShow)
        {
            return (solution, newNames.Count);
        }

        // Erst lesen, dann schreiben - sonst bindet eine Verwendungsstelle ins Leere, sobald ihre
        // Deklaration schon umbenannt ist.
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

    /// <summary>Liefert die vergebenen Umbenennungen, damit der XAML-Durchgang sie nachziehen kann.</summary>
    public static async Task<Dictionary<string, string>> NamesAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects,
        IReadOnlySet<string> exceptions)
    {
        Dictionary<string, string> afterKey =
            await CollectAsync(solution, dictionary, projects, exceptions).ConfigureAwait(false);

        Dictionary<string, string> afterName = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> paar in afterKey)
        {
            // Der Schlüssel endet auf den einfachen Namen; für XAML zählt nur er.
            int separator = paar.Key.LastIndexOf('.');
            string simple = separator < 0 ? paar.Key : paar.Key[(separator + 1)..];
            afterName[simple] = paar.Value;
        }

        return afterName;
    }

    private static async Task<Dictionary<string, string>> CollectAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects,
        IReadOnlySet<string> exceptions)
    {
        Dictionary<string, string> neueNamen = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> occupiedJeType = new(StringComparer.Ordinal);

        foreach (Project project in solution.Projects)
        {
            if (projects is { Length: > 0 } && !projects.Contains(project.Name, StringComparer.Ordinal))
            {
                continue;
            }

            Compilation? compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (INamedTypeSymbol type in AllTypes(compilation.Assembly.GlobalNamespace))
            {
                if (!type.Locations.Any(l => l.IsInSource))
                {
                    continue;
                }

                Check(type, dictionary, exceptions, neueNamen, occupiedJeType);

                foreach (ISymbol member in type.GetMembers())
                {
                    if (!member.Locations.Any(l => l.IsInSource)
                        || member.IsImplicitlyDeclared
                        || member.IsOverride
                        || IstSchnittstellenUmsetzung(member)
                        || member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
                    {
                        continue;
                    }

                    Check(member, dictionary, exceptions, neueNamen, occupiedJeType);
                }
            }
        }

        return neueNamen;
    }

    private static void Check(
        ISymbol symbol,
        IReadOnlyDictionary<string, string> dictionary,
        IReadOnlySet<string> exceptions,
        Dictionary<string, string> newNames,
        Dictionary<string, HashSet<string>> occupiedJeType)
    {
        if (exceptions.Contains(symbol.Name))
        {
            return;
        }

        string? key = symbol.GetDocumentationCommentId();
        if (string.IsNullOrEmpty(key) || newNames.ContainsKey(key))
        {
            return;
        }

        string neu = Translator.Translate(symbol.Name, dictionary);
        if (string.Equals(neu, symbol.Name, StringComparison.Ordinal))
        {
            return;
        }

        // Zwei Member desselben Typs dürfen nicht auf denselben Namen fallen, und ein Member darf
        // nicht heißen wie sein Typ - beides ist ein Compilerfehler.
        ISymbol? containing = symbol.ContainingType as ISymbol ?? symbol.ContainingNamespace;
        string owner = containing?.ToDisplayString() ?? string.Empty;
        if (!occupiedJeType.TryGetValue(owner, out HashSet<string>? occupied))
        {
            occupied = symbol.ContainingType is { } t
                ? [.. t.MemberNames, t.Name]
                : new HashSet<string>(StringComparer.Ordinal);

            occupiedJeType[owner] = occupied;
        }

        if (occupied.Add(neu))
        {
            newNames[key] = neu;
        }
    }

    private static async Task<SyntaxNode?> WriteDocumentAsync(
        Document document,
        IReadOnlyDictionary<string, string> newNames)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync().ConfigureAwait(false);
        if (root is null || model is null)
        {
            return null;
        }

        Dictionary<SyntaxToken, string> replacements = new();

        foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
        {
            switch (node)
            {
                // Konstruktor und Finalizer tragen den Namen ihres Typs. Sie haben keinen eigenen
                // Namen, den man umbenennen könnte - wandert der Typ, müssen sie mitwandern,
                // sonst hält der Compiler den Konstruktor für eine Methode ohne Rückgabetyp.
                case ConstructorDeclarationSyntax konstruktor
                    when model.GetDeclaredSymbol(konstruktor)?.ContainingType is { } konstruktorType:
                    Merke(replacements, konstruktor.Identifier, konstruktorType, newNames);
                    break;

                case DestructorDeclarationSyntax finalizer
                    when model.GetDeclaredSymbol(finalizer)?.ContainingType is { } finalizerType:
                    Merke(replacements, finalizer.Identifier, finalizerType, newNames);
                    break;

                case MemberDeclarationSyntax or EnumMemberDeclarationSyntax:
                    if (model.GetDeclaredSymbol(node) is ISymbol declared
                        && Identifier(node) is { } identifier)
                    {
                        Merke(replacements, identifier, declared, newNames);
                    }

                    break;

                case VariableDeclaratorSyntax field
                    when field.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax:
                    if (model.GetDeclaredSymbol(field) is ISymbol fieldSymbol)
                    {
                        Merke(replacements, field.Identifier, fieldSymbol, newNames);
                    }

                    break;

                // Der positional deklarierte Parameter eines Records ist die Deklarationsstelle der
                // gleichnamigen Eigenschaft. Ohne diesen Fall wandern alle Zugriffe, die Deklaration
                // bleibt stehen - und der Typ hat die Eigenschaft plötzlich nicht mehr.
                case ParameterSyntax positional
                    when positional.Parent?.Parent is TypeDeclarationSyntax
                        && model.GetDeclaredSymbol(positional) is IParameterSymbol positionalSymbol:
                    ISymbol? property = positionalSymbol.ContainingType?
                        .GetMembers(positionalSymbol.Name)
                        .FirstOrDefault(m => m is IPropertySymbol);

                    if (property is not null)
                    {
                        Merke(replacements, positional.Identifier, property, newNames);
                    }

                    break;

                // Verwendungen, nameof, Objektinitialisierer, Attributargumente und <see cref="..."/>
                // sind alle IdentifierNameSyntax - eine Behandlung deckt sie zusammen ab.
                case IdentifierNameSyntax usage:
                    ISymbol? gebunden = model.GetSymbolInfo(usage).Symbol
                        ?? model.GetSymbolInfo(usage).CandidateSymbols.FirstOrDefault();

                    if (gebunden is not null)
                    {
                        Merke(replacements, usage.Identifier, gebunden, newNames);
                    }

                    break;

                case GenericNameSyntax generic:
                    if (model.GetSymbolInfo(generic).Symbol is ISymbol genericSymbol)
                    {
                        Merke(replacements, generic.Identifier, genericSymbol, newNames);
                    }

                    break;

                // new DuplicateGroup(Kopien: ...) - das benannte Argument bindet an den
                // Konstruktorparameter des Records, umbenannt wird aber die Eigenschaft.
                case NameColonSyntax named
                    when model.GetSymbolInfo(named.Name).Symbol is IParameterSymbol namedSymbol:
                    if (PropertyFor(namedSymbol) is { } related)
                    {
                        Merke(replacements, named.Name.Identifier, related, newNames);
                    }

                    break;

                // <param name="Kopien"> an einem Record meint dieselbe Eigenschaft. Ohne diesen
                // Fall meldet der Compiler CS1572 und CS1573.
                case XmlNameAttributeSyntax dokuName:
                    SyntaxNode? ownerNode = dokuName
                        .Ancestors(ascendOutOfTrivia: true)
                        .FirstOrDefault(a => a is MemberDeclarationSyntax);

                    if (ownerNode is not null
                        && model.GetDeclaredSymbol(ownerNode) is INamedTypeSymbol recordType)
                    {
                        ISymbol? fromDoku = recordType
                            .GetMembers(dokuName.Identifier.Identifier.ValueText)
                            .FirstOrDefault(m => m is IPropertySymbol);

                        if (fromDoku is not null)
                        {
                            Merke(replacements, dokuName.Identifier.Identifier, fromDoku, newNames);
                        }
                    }

                    break;
            }
        }

        return replacements.Count == 0
            ? null
            : root.ReplaceTokens(
                replacements.Keys,
                (alt, _) => SyntaxFactory.Identifier(alt.LeadingTrivia, replacements[alt], alt.TrailingTrivia));
    }

    /// <summary>
    /// Zu einem positional deklarierten Record-Parameter die Eigenschaft, die er erzeugt.
    /// </summary>
    private static ISymbol? PropertyFor(IParameterSymbol parameter)
        => parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } konstruktor
            ? konstruktor.ContainingType?.GetMembers(parameter.Name).FirstOrDefault(m => m is IPropertySymbol)
            : null;

    private static SyntaxToken? Identifier(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax typ => typ.Identifier,
        DelegateDeclarationSyntax d => d.Identifier,
        MethodDeclarationSyntax m => m.Identifier,
        PropertyDeclarationSyntax p => p.Identifier,
        EventDeclarationSyntax e => e.Identifier,
        EnumMemberDeclarationSyntax em => em.Identifier,
        _ => null,
    };

    private static void Merke(
        Dictionary<SyntaxToken, string> replacements,
        SyntaxToken token,
        ISymbol symbol,
        IReadOnlyDictionary<string, string> newNames)
    {
        // An der Aufrufstelle steht selten dasselbe Symbol wie an der Deklaration:
        //   snapshot.WithId(...)  - die reduzierte Form der Erweiterungsmethode
        //   Liste<T>.Add(...)     - das konstruierte generische Symbol
        //   umsetzung.Method()    - die Umsetzung statt der Schnittstelle, die den Namen vorgibt
        // Alle drei müssen auf die Deklaration zurückgeführt werden, sonst zeigt der Schlüssel
        // ins Leere und die Fundstelle bleibt stehen.
        ISymbol definition = symbol is IMethodSymbol { ReducedFrom: { } unreduziert }
            ? unreduziert
            : symbol;

        definition = Root(definition.OriginalDefinition);
        string? documentationId = definition.GetDocumentationCommentId();

        if (!string.IsNullOrEmpty(documentationId) && newNames.TryGetValue(documentationId, out string? neu))
        {
            replacements[token] = neu;
        }
    }

    // Die Stelle, an der der Name ursprünglich festgelegt wurde: die Schnittstelle, sonst der
    // oberste überschriebene Member, sonst der Member selbst.
    private static ISymbol Root(ISymbol symbol)
    {
        while (symbol is IMethodSymbol { OverriddenMethod: { } baseMethod })
        {
            symbol = baseMethod;
        }

        while (symbol is IPropertySymbol { OverriddenProperty: { } baseProperty })
        {
            symbol = baseProperty;
        }

        while (symbol is IEventSymbol { OverriddenEvent: { } baseEvent })
        {
            symbol = baseEvent;
        }

        INamedTypeSymbol? typ = symbol.ContainingType;
        if (typ is null)
        {
            return symbol;
        }

        foreach (INamedTypeSymbol schnittstelle in typ.AllInterfaces)
        {
            foreach (ISymbol open in schnittstelle.GetMembers(symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(
                        typ.FindImplementationForInterfaceMember(open), symbol))
                {
                    return open;
                }
            }
        }

        return symbol;
    }

    private static bool IstSchnittstellenUmsetzung(ISymbol member)
        => !SymbolEqualityComparer.Default.Equals(Root(member), member);

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol space)
    {
        foreach (INamedTypeSymbol typ in space.GetTypeMembers())
        {
            yield return typ;

            foreach (INamedTypeSymbol verschachtelt in typ.GetTypeMembers())
            {
                yield return verschachtelt;
            }
        }

        foreach (INamespaceSymbol unterraum in space.GetNamespaceMembers())
        {
            foreach (INamedTypeSymbol typ in AllTypes(unterraum))
            {
                yield return typ;
            }
        }
    }
}
