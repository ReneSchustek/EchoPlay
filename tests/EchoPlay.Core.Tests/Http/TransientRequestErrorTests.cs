using EchoPlay.Core.Http;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EchoPlay.Core.Tests.Http
{
    /// <summary>
    /// Prüft, welche Fehler beim Laden von Anbieterdaten als überspringbar gelten.
    /// </summary>
    /// <remarks>
    /// Diese Unterscheidung entscheidet, ob ein Import weiterläuft oder abbricht. Wird sie
    /// zu weit gefasst, verschluckt sie Programmierfehler; zu eng, und ein einzelnes
    /// kaputtes Album kostet den ganzen Durchlauf.
    /// </remarks>
    public sealed class TransientRequestErrorTests
    {
        public static TheoryData<Exception> SkippableErrors =>
        [
            new HttpRequestException("Gegenstelle nicht erreichbar"),
            new TaskCanceledException("Zeitgrenze überschritten"),
            new JsonException("Antwort nicht lesbar"),
            new InvalidOperationException("Antwort ohne Inhalt"),
            new UriFormatException("Adresse unbrauchbar"),
        ];

        [Theory]
        [MemberData(nameof(SkippableErrors))]
        public void IsTransient_ForAnExpectedLoadingError_SaysYes(Exception ex)
        {
            Assert.True(TransientRequestError.IsTransient(ex));
        }

        [Fact]
        public void IsTransient_ForAProgrammingError_SaysNo()
        {
            // Ein Typfehler ist kein Netzproblem. Ginge er als überspringbar durch,
            // liefe der Import weiter und meldete am Ende zu wenige Folgen, ohne dass
            // irgendwo ein Fehler auftaucht.
            Assert.False(TransientRequestError.IsTransient(new InvalidCastException("falscher Typ")));
        }

        [Fact]
        public void IsTransient_ForAFormatError_SaysNo()
        {
            Assert.False(TransientRequestError.IsTransient(new FormatException("unlesbare Zahl")));
        }
    }
}
