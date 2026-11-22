using System.Text.Json;
using EchoPlay.Core.Replication;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für <see cref="StrictJson"/>: Länge, leere Eingabe und kaputtes JSON führen zum
    /// jeweils übergebenen Grund, gültiges JSON zum Dokument.
    /// </summary>
    public sealed class StrictJsonTests
    {
        private const string TooLarge = "leer oder zu groß";
        private const string Invalid = "kein JSON";

        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        /// <summary>Ein kleines Dokument, an dem das Lesen geprüft wird.</summary>
        private sealed record Sample(int Value, string? Name);

        [Fact]
        public void TryDeserialize_ValidJson_ReturnsDocument()
        {
            string? error = StrictJson.TryDeserialize("{\"value\":7,\"name\":\"x\"}", 100, Options, TooLarge, Invalid, out Sample? document);

            Assert.Null(error);
            Assert.Equal(new Sample(7, "x"), document);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryDeserialize_EmptyInput_ReturnsFirstReason(string? json)
        {
            string? error = StrictJson.TryDeserialize(json, 100, Options, TooLarge, Invalid, out Sample? document);

            Assert.Equal(TooLarge, error);
            Assert.Null(document);
        }

        [Fact]
        public void TryDeserialize_LongerThanLimit_ReturnsFirstReason()
        {
            // Die Grenze greift vor dem Parsen: auch gültiges JSON wird über der Länge abgelehnt.
            string? error = StrictJson.TryDeserialize("{\"value\":7}", 5, Options, TooLarge, Invalid, out Sample? document);

            Assert.Equal(TooLarge, error);
            Assert.Null(document);
        }

        [Fact]
        public void TryDeserialize_ExactlyAtLimit_IsRead()
        {
            const string json = "{\"value\":7}";

            string? error = StrictJson.TryDeserialize(json, json.Length, Options, TooLarge, Invalid, out Sample? document);

            Assert.Null(error);
            Assert.Equal(7, document!.Value);
        }

        [Fact]
        public void TryDeserialize_TruncatedJson_ReturnsSecondReason()
        {
            string? error = StrictJson.TryDeserialize("{\"value\":7", 100, Options, TooLarge, Invalid, out Sample? document);

            Assert.Equal(Invalid, error);
            Assert.Null(document);
        }

        [Fact]
        public void TryDeserialize_JsonNullLiteral_ReadsWithoutDocument()
        {
            // "null" ist gültiges JSON; ob ein fehlendes Dokument zulässig ist, prüft das Format.
            string? error = StrictJson.TryDeserialize("null", 100, Options, TooLarge, Invalid, out Sample? document);

            Assert.Null(error);
            Assert.Null(document);
        }
    }
}
