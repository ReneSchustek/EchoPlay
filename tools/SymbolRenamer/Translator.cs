using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

// Übersetzt einen Bezeichner segmentweise. "eigeneVerweisQuellen" zerfällt in
// eigene|Verweis|Quellen und wird zu ownReferenceSources - die Schreibweise des ersten Segments
// bleibt erhalten, damit aus einer lokalen Variable keine Eigenschaft wird.
//
// Segmente ohne Eintrag im Wörterbuch bleiben stehen. Ein Bezeichner, der kein einziges
// deutsches Segment trägt, kommt unverändert zurück.
internal static class Translator
{
    // Umlaute gehören in Bezeichnern nicht vor - im Bestand stehen sie trotzdem
    // ("AiPrioritaet" als AiPriorität, "GeschätzteAusgabeTokens"). Ohne sie im Muster zerfällt der
    // Name an der falschen Stelle, die Längenprüfung unten schlägt an, und er bliebe für immer
    // stehen. Mit ihnen wird er übersetzt und ist die Umlautfrage los.
    private static readonly Regex Segmentation = new(
        @"[A-ZÄÖÜ]+(?![a-zäöüß])|[A-ZÄÖÜ][a-zäöüß0-9]*|^[a-zäöüß][a-zäöüß0-9]*|_",
        RegexOptions.Compiled);

    public static string Translate(string identifier, IReadOnlyDictionary<string, string> dictionary)
    {
        MatchCollection segmente = Segmentation.Matches(identifier);
        if (segmente.Count == 0)
        {
            return identifier;
        }

        // Nur zusammensetzen, was auch lückenlos zerlegt wurde - sonst fällt bei einem
        // ungewöhnlichen Namen stillschweigend ein Zeichen weg.
        if (segmente.Sum(m => m.Length) != identifier.Length)
        {
            return identifier;
        }

        StringBuilder gebaut = new(identifier.Length);
        bool changed = false;

        for (int i = 0; i < segmente.Count; i++)
        {
            string segment = segmente[i].Value;
            if (!dictionary.TryGetValue(segment.ToLowerInvariant(), out string? english))
            {
                _ = gebaut.Append(segment);
                continue;
            }

            changed = true;
            _ = gebaut.Append(i == 0 && char.IsLower(identifier[0])
                ? char.ToLowerInvariant(english[0]) + english[1..]
                : english);
        }

        if (!changed)
        {
            return identifier;
        }

        // "neu" wird zu "new", "für" zu "for", "diese" zu "this" - aus einem Bezeichner würde
        // ein Schlüsselwort und die Datei wäre nicht mehr übersetzbar. Solche Namen bleiben
        // stehen; sie sind zu wenige, um dafür eine Ausweichregel zu erfinden, die niemand erwartet.
        string result = gebaut.ToString();
        return SyntaxFacts.GetKeywordKind(result) == SyntaxKind.None ? result : identifier;
    }

    public static Dictionary<string, string> LoadDictionary(string path)
    {
        Dictionary<string, string> dictionary = new(StringComparer.Ordinal);

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] parts = trimmed.Split('=', 2);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                dictionary[parts[0].Trim().ToLowerInvariant()] = parts[1].Trim();
            }
        }

        return dictionary;
    }
}
