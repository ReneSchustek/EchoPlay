using System.Text.RegularExpressions;

// Zieht Umbenennungen in den XAML-Dateien nach.
//
// XAML bindet über Zeichenketten, nicht über Symbole - Roslyn sieht davon nichts. Bleibt eine
// Bindung stehen, ist der Build grün und die Oberfläche leer. Das ist die teuerste Fehlerart
// dieses Umbaus, weil sie erst beim Benutzen auffällt.
//
// Drei Stellen tragen Namen:
//   {x:Bind Pfad.Teil}, {Binding Pfad}, {TemplateBinding Pfad}   Eigenschaften und Methoden
//   local:TypName, converters:TypName, x:Class="Raum.TypName"    Typen
//   {x:Bind BearbeitenCommand}                                    von [RelayCommand] erzeugt:
//                                                                 Methode "Bearbeiten" wird zu
//                                                                 "BearbeitenCommand"
internal static class XamlUpdater
{
    public static int Update(string root, IReadOnlyDictionary<string, string> names, bool onlyShow)
    {
        if (names.Count == 0)
        {
            return 0;
        }

        // Für jede umbenannte Methode zusätzlich den Namen, den der Quell-Generator daraus baut.
        Dictionary<string, string> withCommands = new(names, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in names)
        {
            withCommands[$"{pair.Key}Command"] = $"{pair.Value}Command";
        }

        int changed = 0;

        foreach (string file in Files(root))
        {
            string alt = File.ReadAllText(file);
            string neu = ErsetzeIn(alt, withCommands);

            if (!string.Equals(alt, neu, StringComparison.Ordinal))
            {
                changed++;
                if (!onlyShow)
                {
                    File.WriteAllText(file, neu);
                }
            }
        }

        return changed;
    }

    private static IEnumerable<string> Files(string root)
        => Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string ErsetzeIn(string content, IReadOnlyDictionary<string, string> names)
    {
        // Bindungsausdrücke: der Pfad kann mehrere Glieder haben, jedes wird einzeln geprüft.
        content = Regex.Replace(
            content,
            @"\{(x:Bind|Binding|TemplateBinding)((?:\s+|\s+Path=)([A-Za-z_][\w.\[\]]*))",
            matches => matches.Groups[1].Value is { } art
                ? "{" + art + matches.Groups[2].Value.Replace(
                    matches.Groups[3].Value,
                    ReplacePath(matches.Groups[3].Value, names),
                    StringComparison.Ordinal)
                : matches.Value);

        // Typverweise über ein Namensraum-Präfix und die Klasse hinter x:Class.
        content = Regex.Replace(
            content,
            @"(\b\w+:)([A-Z]\w*)",
            matches => names.TryGetValue(matches.Groups[2].Value, out string? neu)
                ? matches.Groups[1].Value + neu
                : matches.Value);

        content = Regex.Replace(
            content,
            @"(x:Class="")([\w.]*\.)?(\w+)("")",
            matches => names.TryGetValue(matches.Groups[3].Value, out string? neu)
                ? matches.Groups[1].Value + matches.Groups[2].Value + neu + matches.Groups[4].Value
                : matches.Value);

        // x:Name erzeugt ein Feld im Code-behind. Wandert das Feld, muss der Name mitwandern -
        // sonst erzeugt der XAML-Compiler weiter das alte Feld und das Code-behind greift ins Leere.
        // ElementName verweist auf denselben Namen und gehört deshalb dazu.
        content = Regex.Replace(
            content,
            @"(x:Name="")(\w+)("")",
            matches => names.TryGetValue(matches.Groups[2].Value, out string? neu)
                ? matches.Groups[1].Value + neu + matches.Groups[3].Value
                : matches.Value);

        content = Regex.Replace(
            content,
            @"(ElementName=)(\w+)",
            matches => names.TryGetValue(matches.Groups[2].Value, out string? neu)
                ? matches.Groups[1].Value + neu
                : matches.Value);

        return content;
    }

    private static string ReplacePath(string path, IReadOnlyDictionary<string, string> names)
        => string.Join('.', path.Split('.').Select(glied =>
        {
            // Ein Glied kann einen Indexzugriff tragen: Items[0]
            int bracket = glied.IndexOf('[', StringComparison.Ordinal);
            string name = bracket < 0 ? glied : glied[..bracket];
            string rest = bracket < 0 ? string.Empty : glied[bracket..];

            return names.TryGetValue(name, out string? neu) ? neu + rest : glied;
        }));
}
