using System.Security.Cryptography;

namespace EchoPlay.Core.Security
{
    /// <summary>
    /// Prüft die Echtheit einer Setup-Datei über ihre losgelöste ECDSA-P-256-Signatur (SHA-256).
    /// </summary>
    /// <remarks>
    /// Ein Hash aus dem Release-Text belegt nur, dass die Datei unterwegs nicht verändert wurde.
    /// Wer den Release-Eintrag selbst austauschen kann, tauscht Datei und Hash zusammen aus. Eine
    /// Signatur dagegen entsteht nur mit dem privaten Schlüssel, und der liegt nicht bei GitHub im
    /// Release, sondern offline und als Secret der Release-Pipeline. Eingebettet ist hier allein
    /// der öffentliche Teil; wer ihn kennt, kann prüfen, aber nichts signieren.
    /// <para>
    /// Die Signatur kommt im Format IEEE P1363 (r und s je 32 Byte hintereinander), so wie
    /// <see cref="ECDsa.SignData(byte[], HashAlgorithmName)"/> sie ohne weitere Angabe schreibt,
    /// also auch <c>tools/sign-release.ps1</c>.
    /// </para>
    /// </remarks>
    public sealed class ReleaseSignatureVerifier
    {
        /// <summary>
        /// Öffentlicher Schlüssel der Release-Signatur (ECDSA P-256, SubjectPublicKeyInfo, Base64).
        /// Erzeugt mit <c>tools/new-signing-key.ps1</c>.
        /// </summary>
        /// <remarks>
        /// Ein neuer Schlüssel hier schneidet alle installierten Fassungen vom Auto-Update ab: Sie
        /// vertrauen nur dem Schlüssel, mit dem sie gebaut wurden, und lehnen jede neue Signatur ab,
        /// bis der Anwender eine Fassung mit dem neuen Schlüssel von Hand installiert.
        /// </remarks>
        public const string DefaultPublicKeySpkiBase64 =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEioiMOEUOev9P8HRavHR7tUuwA6gP/RWypBgCJX/aVVMsmsooFVKSQsk1GEgOd8cYjUf/8hQqttj1edJyU0tyOw==";

        /// <summary>Länge einer P-256-Signatur im Format IEEE P1363: r und s je 32 Byte.</summary>
        public const int SignatureLength = 64;

        private const int CurveBits = 256;
        private const int Sha256DigestLength = 32;

        private readonly byte[] _publicKeySpki;

        /// <summary>Erzeugt den Prüfer mit dem eingebetteten Release-Schlüssel.</summary>
        public ReleaseSignatureVerifier()
            : this(DefaultPublicKeySpkiBase64)
        {
        }

        /// <summary>Erzeugt den Prüfer mit einem ausdrücklich genannten öffentlichen Schlüssel.</summary>
        /// <param name="publicKeySpkiBase64">Öffentlicher P-256-Schlüssel als SubjectPublicKeyInfo in Base64.</param>
        /// <exception cref="ArgumentException">Der Schlüssel ist leer, kein Base64 oder kein P-256-Schlüssel.</exception>
        public ReleaseSignatureVerifier(string publicKeySpkiBase64)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(publicKeySpkiBase64);

            // Ein unbrauchbarer Schlüssel fällt hier auf, beim Aufbau der Dienste, und nicht erst
            // beim ersten Update, wo er nur als „Signatur ungültig“ im Protokoll stünde.
            try
            {
                _publicKeySpki = Convert.FromBase64String(publicKeySpkiBase64);
                using ECDsa probe = ImportKey();
                if (probe.KeySize != CurveBits)
                {
                    throw new ArgumentException($"Erwartet wird ein P-256-Schlüssel, geliefert wurden {probe.KeySize} Bit.", nameof(publicKeySpkiBase64));
                }
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Der öffentliche Schlüssel ist kein Base64.", nameof(publicKeySpkiBase64), ex);
            }
            catch (CryptographicException ex)
            {
                throw new ArgumentException("Der öffentliche Schlüssel ist kein gültiger ECDSA-Schlüssel.", nameof(publicKeySpkiBase64), ex);
            }
        }

        /// <summary>
        /// Prüft, ob <paramref name="signature"/> eine gültige Signatur der Datei ist, deren
        /// SHA-256 <paramref name="sha256Digest"/> ist.
        /// </summary>
        /// <param name="sha256Digest">SHA-256 der ganzen Datei (32 Byte).</param>
        /// <param name="signature">Die rohen Signaturbytes aus der <c>.sig</c>-Datei.</param>
        /// <returns><see langword="true"/> nur bei gültiger Signatur.</returns>
        /// <remarks>
        /// Der Aufrufer bildet den Hash selbst, damit er die Datei nur einmal liest und dabei
        /// gesperrt halten kann: Zwischen einer Prüfung per Pfad und dem Start des Installers
        /// ließe sich die Datei sonst austauschen.
        /// </remarks>
        /// <exception cref="ArgumentException">Der Hash ist nicht 32 Byte lang.</exception>
        public bool VerifyDigest(ReadOnlySpan<byte> sha256Digest, ReadOnlySpan<byte> signature)
        {
            if (sha256Digest.Length != Sha256DigestLength)
            {
                throw new ArgumentException($"Erwartet wird ein SHA-256 mit {Sha256DigestLength} Byte.", nameof(sha256Digest));
            }

            // Jede andere Länge ist keine P1363-Signatur dieses Schlüssels; sie wird abgelehnt,
            // ohne dass die Kryptografie sie zu deuten versucht.
            if (signature.Length != SignatureLength)
            {
                return false;
            }

            using ECDsa ecdsa = ImportKey();
            return ecdsa.VerifyHash(sha256Digest, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        // Je Prüfung eine eigene Instanz: Der Prüfer ist ein Singleton, und für eine gemeinsam
        // genutzte ECDsa-Instanz sagt .NET keine Threadsicherheit zu. Der Import kostet bei
        // einer Prüfung je Update nichts Messbares.
        private ECDsa ImportKey()
        {
            ECDsa ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(_publicKeySpki, out _);
                return ecdsa;
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }
    }
}
