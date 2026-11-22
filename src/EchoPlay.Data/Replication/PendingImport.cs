using System.Text.Json;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Die Markierung neben der Datenbank: Beim nächsten Start ist ein vorbereitetes Paket
    /// einzuspielen.
    /// </summary>
    /// <remarks>
    /// Die Markierung trägt nur, was nach dem Ersetzen noch gebraucht wird, und kein Geheimnis:
    /// Zugangsdaten liegen, falls gewählt, getrennt und per DPAPI verschlüsselt daneben
    /// (<see cref="CredentialsFileName"/>). Fehlt die entpackte Datenbank beim Start, wird die
    /// Markierung verworfen; ein halb vorbereitetes Einspielen läuft nie.
    /// </remarks>
    /// <param name="PackageLibraryRoot">Stammpfad der Mediathek beim Absender, für die Umrechnung.</param>
    /// <param name="SourceDevice">Geräte-Kennung des Absenders, für das Protokoll.</param>
    /// <param name="LibraryRootOverride">
    /// Neuer Stammpfad dieses Rechners, wenn mit dem Paket auch die Mediathek ausgepackt wurde; dann
    /// gilt er statt des bisherigen, und die Pfade werden auf ihn umgerechnet. Sonst leer.
    /// </param>
    public sealed record PendingImport(string? PackageLibraryRoot, Guid SourceDevice, string? LibraryRootOverride = null)
    {
        /// <summary>Name der Markierung im Datenverzeichnis.</summary>
        public const string FileName = "import.pending";

        /// <summary>Name der DPAPI-verschlüsselten Zugangsdaten im Datenverzeichnis.</summary>
        public const string CredentialsFileName = "import.credentials";

        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        /// <summary>Schreibt die Markierung.</summary>
        /// <param name="directory">Das Datenverzeichnis.</param>
        public void Write(string directory)
        {
            string path = Path.Combine(directory, FileName);
            string partial = path + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(this, Options));
            File.Move(partial, path, overwrite: true);
        }

        /// <summary>Liest die Markierung, falls eine da ist.</summary>
        /// <param name="directory">Das Datenverzeichnis.</param>
        /// <returns>Die Markierung, oder <see langword="null"/>, wenn keine da oder sie unlesbar ist.</returns>
        public static PendingImport? TryRead(string directory)
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PendingImport>(File.ReadAllText(path), Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Entfernt Markierung, entpackte Datenbank und Zugangsdaten, etwa nach einem Abbruch.</summary>
        /// <param name="directory">Das Datenverzeichnis.</param>
        public static void Discard(string directory)
        {
            foreach (string name in new[] { FileName, CredentialsFileName, PackageStager.StagedFileName })
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
