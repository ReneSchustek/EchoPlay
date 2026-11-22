using System.Security.Cryptography;
using EchoPlay.Core.Security;

namespace EchoPlay.Core.Tests.Security
{
    /// <summary>
    /// Verifiziert die Prüfung der losgelösten Release-Signatur: Nur eine Signatur des passenden
    /// Schlüssels über genau diese Bytes und im Format, das <c>tools/sign-release.ps1</c> schreibt,
    /// wird angenommen.
    /// </summary>
    public sealed class ReleaseSignatureVerifierTests
    {
        private static readonly byte[] Payload = [0x4D, 0x5A, 1, 2, 3, 4, 5, 6, 7, 8];

        [Fact]
        public void VerifyDigest_SignatureOfTheSameBytes_IsAccepted()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(key);

            // SignData ohne Formatangabe ist genau der Aufruf des Signierskripts.
            byte[] signature = key.SignData(Payload, HashAlgorithmName.SHA256);

            Assert.True(verifier.VerifyDigest(SHA256.HashData(Payload), signature));
        }

        [Fact]
        public void VerifyDigest_OneByteChanged_IsRejected()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(key);
            byte[] signature = key.SignData(Payload, HashAlgorithmName.SHA256);

            byte[] tampered = (byte[])Payload.Clone();
            tampered[^1] ^= 0x01;

            Assert.False(verifier.VerifyDigest(SHA256.HashData(tampered), signature));
        }

        [Fact]
        public void VerifyDigest_SignatureOfAnotherKey_IsRejected()
        {
            // Der Fall, gegen den die Signatur da ist: Wer den Release-Eintrag austauscht, kann
            // signieren, aber nur mit einem eigenen Schlüssel.
            using ECDsa trusted = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(trusted);

            byte[] signature = attacker.SignData(Payload, HashAlgorithmName.SHA256);

            Assert.False(verifier.VerifyDigest(SHA256.HashData(Payload), signature));
        }

        [Fact]
        public void VerifyDigest_DerEncodedSignature_IsRejected()
        {
            // Das Format ist festgelegt. Eine DER-Signatur ist inhaltlich gültig, hat aber eine
            // andere Länge; sie anzunehmen hieße, zwei Formate zu pflegen.
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(key);
            byte[] der = key.SignData(Payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            Assert.False(verifier.VerifyDigest(SHA256.HashData(Payload), der));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(63)]
        [InlineData(65)]
        [InlineData(128)]
        public void VerifyDigest_SignatureOfWrongLength_IsRejected(int length)
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(key);

            Assert.False(verifier.VerifyDigest(SHA256.HashData(Payload), new byte[length]));
        }

        [Fact]
        public void VerifyDigest_ZeroSignatureOfRightLength_IsRejected()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(key);

            Assert.False(verifier.VerifyDigest(SHA256.HashData(Payload), new byte[ReleaseSignatureVerifier.SignatureLength]));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(20)]
        [InlineData(64)]
        public void VerifyDigest_DigestThatIsNoSha256_Throws(int length)
        {
            // Ein falsch langer Hash ist ein Fehler im Aufrufer, kein abgelehntes Update.
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ReleaseSignatureVerifier verifier = VerifierFor(key);

            _ = Assert.Throws<ArgumentException>(() => verifier.VerifyDigest(new byte[length], new byte[ReleaseSignatureVerifier.SignatureLength]));
        }

        [Fact]
        public void Constructor_EmbeddedKey_IsAUsableP256Key()
        {
            // Ein verschriebener Schlüssel im Quelltext fiele sonst erst beim ersten Update auf,
            // als jede Signatur „ungültig“.
            ReleaseSignatureVerifier verifier = new();

            Assert.False(verifier.VerifyDigest(SHA256.HashData(Payload), new byte[ReleaseSignatureVerifier.SignatureLength]));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("kein Base64!")]
        [InlineData("AAAA")]
        public void Constructor_UnusableKey_Throws(string publicKey)
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => new ReleaseSignatureVerifier(publicKey));
        }

        [Fact]
        public void Constructor_KeyOfAnotherCurve_Throws()
        {
            // P-384 ist ein gültiger ECDSA-Schlüssel, passt aber nicht zum festgelegten Verfahren.
            using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            string spki = Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo());

            _ = Assert.Throws<ArgumentException>(() => new ReleaseSignatureVerifier(spki));
        }

        [Fact]
        public void Constructor_RsaKey_Throws()
        {
            using RSA rsa = RSA.Create(2048);
            string spki = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

            _ = Assert.Throws<ArgumentException>(() => new ReleaseSignatureVerifier(spki));
        }

        private static ReleaseSignatureVerifier VerifierFor(ECDsa key)
            => new(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }
}
