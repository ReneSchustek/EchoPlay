using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>
    /// Ein Eintrag des Pakets mit Länge und Prüfsumme.
    /// </summary>
    /// <param name="Name">Name im Archiv, einer der Namen aus <see cref="PackageFormat"/>.</param>
    /// <param name="Length">Länge entpackt, in Byte.</param>
    /// <param name="Sha256">SHA-256 des entpackten Inhalts, 64 Kleinbuchstaben-Hexziffern.</param>
    public sealed record PackageEntry(string Name, long Length, string Sha256);

    /// <summary>
    /// Das Manifest eines Pakets: was drin ist, woher es kommt und für welchen Stand es gilt.
    /// </summary>
    /// <remarks>
    /// Das Format ist versioniert und als JSON lesbar, weil der Abgleich es als Ausgangsstand
    /// wiederverwendet und eine Anwendung auf einer anderen Plattform es lesen können soll. Gelesen
    /// wird streng: Ein fremdes Paket ist nicht vertrauenswürdig, und ein halb verstandenes Manifest
    /// ist gefährlicher als ein abgelehntes.
    /// </remarks>
    /// <param name="FormatVersion">Version des Paketformats.</param>
    /// <param name="AppVersion">Fassung der Anwendung, die das Paket geschrieben hat.</param>
    /// <param name="LastMigration">Letzte Migration der Datenbank im Paket.</param>
    /// <param name="DeviceId">Geräte-Kennung des Absenders.</param>
    /// <param name="CreatedUtc">Zeitpunkt des Exports.</param>
    /// <param name="LibraryRoot">Stammpfad der Mediathek beim Absender, falls gesetzt.</param>
    /// <param name="Entries">Die Einträge des Pakets außer dem Manifest selbst.</param>
    public sealed record PackageManifest(
        int FormatVersion,
        string AppVersion,
        string LastMigration,
        Guid DeviceId,
        DateTimeOffset CreatedUtc,
        string? LibraryRoot,
        IReadOnlyList<PackageEntry> Entries)
    {
        // Umlaute im Stammpfad bleiben lesbar; Zeichen, die in HTML gefährlich wären, maskiert der
        // Encoder weiterhin.
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        };
        private static readonly Regex MigrationId = new(@"^\d{14}_[A-Za-z0-9]{1,120}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex HexSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>Schreibt das Manifest als JSON.</summary>
        /// <returns>Das JSON, eingerückt, UTF-8-tauglich.</returns>
        public string ToJson() => JsonSerializer.Serialize(new ManifestDocument(
            FormatVersion,
            AppVersion,
            LastMigration,
            DeviceId.ToString("N"),
            CreatedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            LibraryRoot,
            [.. Entries.Select(e => new EntryDocument(e.Name, e.Length, e.Sha256))]), Options);

        /// <summary>
        /// Liest ein Manifest und prüft jedes Feld.
        /// </summary>
        /// <param name="json">Der Inhalt von <see cref="PackageFormat.ManifestEntry"/>.</param>
        /// <param name="manifest">Das gelesene Manifest.</param>
        /// <param name="error">Der Grund der Ablehnung, für das Protokoll.</param>
        /// <returns><see langword="true"/>, wenn das Manifest gültig ist.</returns>
        public static bool TryParse(string? json, [NotNullWhen(true)] out PackageManifest? manifest, out string? error)
        {
            manifest = null;
            error = StrictJson.TryDeserialize(
                json, PackageFormat.MaxManifestLength, Options,
                "Manifest fehlt oder ist zu groß.", "Manifest ist kein gültiges JSON.",
                out ManifestDocument? document)
                ?? Validate(document);
            if (error is not null)
            {
                return false;
            }

            // Validate hat alle Pflichtfelder auf null geprüft; ab hier ist das Dokument vollständig.
            manifest = new PackageManifest(
                document!.FormatVersion,
                document.AppVersion!,
                document.LastMigration!,
                Guid.ParseExact(document.DeviceId!, "N"),
                DateTimeOffset.Parse(document.CreatedUtc!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                string.IsNullOrWhiteSpace(document.LibraryRoot) ? null : document.LibraryRoot,
                [.. document.Entries!.Select(e => new PackageEntry(e.Name!, e.Length, e.Sha256!))]);
            return true;
        }

        private static string? Validate(ManifestDocument? d)
        {
            if (d is null)
            {
                return "Manifest ist leer.";
            }

            if (d.FormatVersion != PackageFormat.CurrentFormatVersion)
            {
                return $"Paketformat {d.FormatVersion} wird nicht unterstützt.";
            }

            if (string.IsNullOrWhiteSpace(d.AppVersion) || d.AppVersion.Length > 64)
            {
                return "Fassung der Anwendung fehlt.";
            }

            if (d.LastMigration is null || !MigrationId.IsMatch(d.LastMigration))
            {
                return "Letzte Migration fehlt oder hat keine gültige Form.";
            }

            return ValidateOrigin(d) ?? ValidateEntries(d.Entries);
        }

        /// <summary>Prüft, woher und wann das Paket kam und wo die Mediathek dort lag.</summary>
        private static string? ValidateOrigin(ManifestDocument d)
        {
            if (!Guid.TryParseExact(d.DeviceId, "N", out Guid device) || device == Guid.Empty)
            {
                return "Geräte-Kennung fehlt.";
            }

            if (!DateTimeOffset.TryParse(d.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            {
                return "Zeitpunkt des Exports fehlt.";
            }

            return d.LibraryRoot is { Length: > 0 } root && (root.Length > 1024 || !Path.IsPathFullyQualified(root))
                ? "Stammpfad im Manifest ist nicht absolut."
                : null;
        }

        /// <summary>
        /// Prüft die Einträge: nur bekannte Namen, jeder einmal, mit Länge und Prüfsumme, und die
        /// Datenbank muss dabei sein.
        /// </summary>
        private static string? ValidateEntries(IReadOnlyList<EntryDocument>? entries)
        {
            if (entries is null || entries.Count == 0 || entries.Count > PackageFormat.MaxEntries)
            {
                return "Einträge fehlen.";
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (EntryDocument entry in entries)
            {
                if (entry.Name is null || !PackageFormat.IsContentEntry(entry.Name) || !seen.Add(entry.Name))
                {
                    return $"Unerwarteter oder doppelter Eintrag: {entry.Name}";
                }

                if (entry.Length < 0 || entry.Sha256 is null || !HexSha256.IsMatch(entry.Sha256))
                {
                    return $"Eintrag {entry.Name} ohne gültige Länge oder Prüfsumme.";
                }
            }

            return seen.Contains(PackageFormat.DatabaseEntry) ? null : "Die Datenbank fehlt im Paket.";
        }

        /// <summary>Die JSON-Form des Manifests.</summary>
        private sealed record ManifestDocument(
            int FormatVersion,
            string? AppVersion,
            string? LastMigration,
            string? DeviceId,
            string? CreatedUtc,
            string? LibraryRoot,
            List<EntryDocument>? Entries);

        /// <summary>Die JSON-Form eines Eintrags.</summary>
        private sealed record EntryDocument(string? Name, long Length, string? Sha256);
    }
}
