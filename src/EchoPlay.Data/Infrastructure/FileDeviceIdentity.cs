using EchoPlay.Core;
using EchoPlay.Core.Replication;

namespace EchoPlay.Data.Infrastructure
{
    /// <summary>
    /// Die Geräte-Kennung als Datei <c>device.id</c> neben der Datenbank.
    /// </summary>
    /// <remarks>
    /// Bewusst nicht in der Datenbank: Spielt jemand ein Paket von einem anderen Rechner ein, wird
    /// die Datenbank ersetzt, die Datei daneben nicht. Jedes Gerät behält so seine Kennung.
    /// Die Datei entsteht beim ersten Zugriff. Ist sie unlesbar oder ihr Inhalt keine Kennung, wird
    /// eine neue vergeben und die Datei überschrieben; das Gerät erscheint dem Abgleich dann als neu,
    /// was nichts verliert, sondern nur einen weiteren Absender anlegt.
    /// </remarks>
    public sealed class FileDeviceIdentity : IDeviceIdentity
    {
        /// <summary>Name der Datei im Datenverzeichnis.</summary>
        public const string FileName = "device.id";

        private readonly Lazy<Guid> _deviceId;

        /// <summary>
        /// Erzeugt die Kennung für ein Datenverzeichnis.
        /// </summary>
        /// <param name="directory">Das Verzeichnis, in dem die Datenbank liegt.</param>
        public FileDeviceIdentity(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            string path = Path.Combine(directory, FileName);
            _deviceId = new Lazy<Guid>(() => LoadOrCreate(path), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public Guid DeviceId => _deviceId.Value;

        private static Guid LoadOrCreate(string path)
        {
            if (TryRead(path, out Guid existing))
            {
                return existing;
            }

            Guid created = Guid.NewGuid();
            _ = Directory.CreateDirectory(ParentFolder.Of(path));
            File.WriteAllText(path, created.ToString("N"));
            return created;
        }

        private static bool TryRead(string path, out Guid id)
        {
            id = Guid.Empty;
            try
            {
                return File.Exists(path)
                    && Guid.TryParseExact(File.ReadAllText(path).Trim(), "N", out id)
                    && id != Guid.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
