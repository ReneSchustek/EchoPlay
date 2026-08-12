using Microsoft.CodeAnalysis;

// Sammelt Typen, Member und Parameter, deren Name ein deutsches Wortsegment trägt, und liefert
// sie als Umbenennungsaufträge. Die Aufträge laufen anschließend durch dieselbe Roslyn-Schleife
// wie handgeschriebene - damit wandern benannte Argumente und <param name="..."> mit.
//
// Warum nicht wie bei den lokalen Bezeichnern dokumentweise: Ein Member ist von überall
// erreichbar. Ein Textersatz im Deklarationsdokument ließe jeden Aufrufer in einem anderen
// Projekt stehen.
internal static class MemberCollector
{
    public static async Task<List<(string Kind, string? Container, string From, string To, string Projekt)>> CollectAsync(
        Solution solution,
        IReadOnlyDictionary<string, string> dictionary,
        string[]? projects)
    {
        List<(string, string?, string, string, string)> jobs = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

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

                Check(type, "type", null, project.Name, dictionary, seen, jobs);

                foreach (ISymbol member in type.GetMembers())
                {
                    if (!member.Locations.Any(l => l.IsInSource) || member.IsImplicitlyDeclared)
                    {
                        continue;
                    }

                    // Überschriebene und Interface-Umsetzungen erben ihren Namen. Wird die
                    // Basis umbenannt, wandert die Ableitung mit; einzeln umbenannt entstünde
                    // eine zweite, unverbundene Methode.
                    if (member.IsOverride || IstSchnittstellenUmsetzung(member))
                    {
                        continue;
                    }

                    Check(member, "member", type.Name, project.Name, dictionary, seen, jobs);
                }
            }
        }

        return jobs;
    }

    private static void Check(
        ISymbol symbol,
        string kind,
        string? container,
        string project,
        IReadOnlyDictionary<string, string> dictionary,
        HashSet<string> seen,
        List<(string, string?, string, string, string)> jobs)
    {
        string name = symbol.Name;
        if (name.Length == 0 || name[0] == '.')
        {
            return;
        }

        string neu = Translator.Translate(name, dictionary);
        if (string.Equals(neu, name, StringComparison.Ordinal))
        {
            return;
        }

        // Derselbe Name kann in mehreren Typen stehen. Der Auftrag wird trotzdem nur einmal
        // aufgenommen - die Rename-Schleife arbeitet ihn so lange ab, bis kein Treffer mehr da ist.
        string key = $"{kind}|{container}|{name}";
        if (seen.Add(key))
        {
            jobs.Add((kind, container, name, neu, project));
        }
    }

    private static bool IstSchnittstellenUmsetzung(ISymbol member)
    {
        INamedTypeSymbol? typ = member.ContainingType;
        if (typ is null)
        {
            return false;
        }

        foreach (INamedTypeSymbol schnittstelle in typ.AllInterfaces)
        {
            foreach (ISymbol open in schnittstelle.GetMembers(member.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(typ.FindImplementationForInterfaceMember(open), member))
                {
                    return true;
                }
            }
        }

        return false;
    }

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
