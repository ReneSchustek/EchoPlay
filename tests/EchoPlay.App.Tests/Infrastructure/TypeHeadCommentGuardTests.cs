using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Hält fest, dass jeder Typ im Quellbaum einen Kopf trägt: eine XML-Doku mit Text im
    /// <c>summary</c> oder ein <c>inheritdoc</c>. Das gilt auch für verschachtelte Typen und die
    /// Hilfsklassen der Tests.
    /// <para>
    /// Ein einfacher <c>//</c>-Kommentar über dem Typ zählt nicht. Er taucht in keiner Doku und
    /// keinem Tooltip auf, und die Kommentar-Regel verlangt für den Typ ausdrücklich XML-Doku.
    /// Bei partiellen Typen genügt der Kopf an einem Teil.
    /// </para>
    /// </summary>
    public sealed class TypeHeadCommentGuardTests
    {
        [Fact]
        public void EveryType_CarriesAHeadComment()
        {
            Dictionary<string, string> sources = SourceTreeFiles.Enumerate()
                .ToDictionary(SourceTreeFiles.Relative, File.ReadAllText, StringComparer.Ordinal);

            IReadOnlyList<string> findings = FindTypesWithoutHead(sources);

            Assert.True(findings.Count == 0,
                "Typ ohne Kopf-Kommentar (kommentare.md, Typ-Ebene):\n" + string.Join('\n', findings));
        }

        /// <summary>
        /// Belegt, dass die Suche den echten Quellbaum durchläuft. Ohne diese Probe stünde der Wächter
        /// nach einem Umzug der Ordner grün, weil er nichts mehr findet.
        /// </summary>
        [Fact]
        public void Guard_ScansTheRealSourceTree()
        {
            string[] files = [.. SourceTreeFiles.Enumerate()];

            Assert.Contains(files, file => file.EndsWith(Path.Combine("Services", "PlayerService.cs"), StringComparison.Ordinal));
            Assert.Contains(files, file => file.EndsWith(Path.Combine("Infrastructure", "TypeHeadCommentGuardTests.cs"), StringComparison.Ordinal));
        }

        [Fact]
        public void FindTypesWithoutHead_ReportsTypeWithoutComment()
        {
            IReadOnlyList<string> findings = Check("namespace N { public sealed class Bare { } }");

            string finding = Assert.Single(findings);
            Assert.Contains("Bare", finding, StringComparison.Ordinal);
        }

        [Fact]
        public void FindTypesWithoutHead_ReportsNestedTypeWithoutComment()
        {
            IReadOnlyList<string> findings = Check(
                "namespace N { /// <summary>Außen.</summary>\n public sealed class Outer { private sealed class Inner { } } }");

            string finding = Assert.Single(findings);
            Assert.Contains("Outer.Inner", finding, StringComparison.Ordinal);
        }

        [Fact]
        public void FindTypesWithoutHead_ReportsLineCommentAsMissingHead()
        {
            IReadOnlyList<string> findings = Check("namespace N {\n// Nur ein Zeilenkommentar.\npublic enum Mode { A }\n}");

            _ = Assert.Single(findings);
        }

        [Fact]
        public void FindTypesWithoutHead_ReportsEmptySummary()
        {
            IReadOnlyList<string> findings = Check("namespace N {\n/// <summary>\n/// </summary>\npublic interface IEmpty { }\n}");

            _ = Assert.Single(findings);
        }

        [Fact]
        public void FindTypesWithoutHead_AcceptsSummaryAndInheritdoc()
        {
            IReadOnlyList<string> findings = Check(
                "namespace N {\n/// <summary>Ein Kopf.</summary>\npublic record R(int A);\n/// <inheritdoc/>\npublic delegate void D();\n}");

            Assert.Empty(findings);
        }

        [Fact]
        public void FindTypesWithoutHead_AcceptsPartialTypeWithHeadOnOnePart()
        {
            Dictionary<string, string> sources = new(StringComparer.Ordinal)
            {
                ["A.cs"] = "namespace N {\n/// <summary>Kopf am ersten Teil.</summary>\npublic partial class Split { }\n}",
                ["B.cs"] = "namespace N { public partial class Split { } }",
            };

            Assert.Empty(FindTypesWithoutHead(sources));
        }

        [Fact]
        public void FindTypesWithoutHead_KeepsEqualNamesInOtherNamespacesApart()
        {
            Dictionary<string, string> sources = new(StringComparer.Ordinal)
            {
                ["A.cs"] = "namespace N1 {\n/// <summary>Kopf.</summary>\npublic partial class Same { }\n}",
                ["B.cs"] = "namespace N2 { public partial class Same { } }",
            };

            string finding = Assert.Single(FindTypesWithoutHead(sources));
            Assert.Contains("N2.Same", finding, StringComparison.Ordinal);
        }

        private static IReadOnlyList<string> Check(string source) =>
            FindTypesWithoutHead(new Dictionary<string, string>(StringComparer.Ordinal) { ["Probe.cs"] = source });

        /// <summary>
        /// Sucht Typen ohne Kopf. Teile eines partiellen Typs werden über ihren vollen Namen
        /// zusammengefasst; gemeldet wird erst, wenn keiner der Teile einen Kopf hat.
        /// </summary>
        /// <param name="sources">Quelltexte, nach Dateiname.</param>
        /// <returns>Eine Zeile je Typ ohne Kopf, mit Datei, Zeile und vollem Namen.</returns>
        private static IReadOnlyList<string> FindTypesWithoutHead(IReadOnlyDictionary<string, string> sources)
        {
            Dictionary<string, (bool HasHead, string FirstLocation)> types = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> source in sources)
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(source.Value, path: source.Key);
                IEnumerable<MemberDeclarationSyntax> declarations = tree.GetRoot().DescendantNodes()
                    .OfType<MemberDeclarationSyntax>()
                    .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

                foreach (MemberDeclarationSyntax declaration in declarations)
                {
                    string name = FullName(declaration);
                    int line = tree.GetLineSpan(declaration.Span).StartLinePosition.Line + 1;
                    bool hasHead = HasHead(declaration);

                    types[name] = types.TryGetValue(name, out (bool HasHead, string FirstLocation) known)
                        ? (known.HasHead || hasHead, known.FirstLocation)
                        : (hasHead, $"{source.Key}:{line}");
                }
            }

            return [.. types
                .Where(entry => !entry.Value.HasHead)
                .Select(entry => $"{entry.Value.FirstLocation} {entry.Key}")
                .OrderBy(finding => finding, StringComparer.Ordinal)];
        }

        private static bool HasHead(MemberDeclarationSyntax declaration)
        {
            IEnumerable<DocumentationCommentTriviaSyntax> docs = declaration.GetLeadingTrivia()
                .Select(trivia => trivia.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>();

            foreach (DocumentationCommentTriviaSyntax doc in docs)
            {
                if (doc.Content.OfType<XmlEmptyElementSyntax>().Any(element => element.Name.LocalName.Text == "inheritdoc"))
                {
                    return true;
                }

                // Ein leeres summary sieht im Editor wie ein Kopf aus, sagt aber nichts.
                bool summaryWithText = doc.Content.OfType<XmlElementSyntax>()
                    .Where(element => element.StartTag.Name.LocalName.Text == "summary")
                    .Any(element => !string.IsNullOrWhiteSpace(
                        string.Concat(element.Content.Select(part => part.ToString())).Replace("///", string.Empty, StringComparison.Ordinal)));
                if (summaryWithText)
                {
                    return true;
                }
            }

            return false;
        }

        // Namensraum, umgebende Typen und Anzahl der Typparameter, damit gleich benannte Typen in
        // verschiedenen Räumen oder mit anderer Stelligkeit nicht verschmelzen.
        private static string FullName(MemberDeclarationSyntax declaration)
        {
            IEnumerable<string> parts = declaration.AncestorsAndSelf()
                .Reverse()
                .Select(node => node switch
                {
                    BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
                    TypeDeclarationSyntax type => type.Identifier.Text + Arity(type.TypeParameterList),
                    BaseTypeDeclarationSyntax type => type.Identifier.Text,
                    DelegateDeclarationSyntax del => del.Identifier.Text + Arity(del.TypeParameterList),
                    _ => null,
                })
                .OfType<string>();

            return string.Join('.', parts);
        }

        private static string Arity(TypeParameterListSyntax? parameters) =>
            parameters is null ? string.Empty : "`" + parameters.Parameters.Count;
    }
}
