using EchoPlay.Core.Http;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
            Assert.True(TransientRequestError.IsTransient(ex, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void IsTransient_ForAProgrammingError_SaysNo()
        {
            // Ein Typfehler ist kein Netzproblem. Ginge er als überspringbar durch,
            // liefe der Import weiter und meldete am Ende zu wenige Folgen, ohne dass
            // irgendwo ein Fehler auftaucht.
            Assert.False(TransientRequestError.IsTransient(new InvalidCastException("falscher Typ"), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void IsTransient_ForAFormatError_SaysNo()
        {
            Assert.False(TransientRequestError.IsTransient(new FormatException("unlesbare Zahl"), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void IsTransient_WhenTheOperationWasCancelled_SaysNo()
        {
            // Ein abgebrochener Aufruf meldet sich mit derselben Ausnahme wie eine
            // Zeitüberschreitung. Ohne den Blick auf das Abbruchzeichen schrieb die Suche
            // für jeden verbliebenen Künstler eine Warnung über einen Ausfall, den es nie gab.
            using CancellationTokenSource cts = new();
            cts.Cancel();

            Assert.False(TransientRequestError.IsTransient(new TaskCanceledException("abgebrochen"), cts.Token));
        }

        [Fact]
        public void IsTransient_ForATimeoutWithoutCancellation_SaysYes()
        {
            // Die Gegenstelle hat nicht rechtzeitig geantwortet, niemand hat abgebrochen —
            // das bleibt ein überspringbarer Ladefehler.
            Assert.True(TransientRequestError.IsTransient(
                new TaskCanceledException("Zeitgrenze überschritten"), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void IsTransient_ForANetworkErrorDuringCancellation_StillSaysYes()
        {
            // Die Ausnahme gilt nur dem Abbruch selbst. Ein echter Netzfehler bleibt ein
            // Netzfehler, auch wenn zufällig gerade abgebrochen wurde — sonst verschwänden
            // Ausfälle genau dann, wenn viel los ist.
            using CancellationTokenSource cts = new();
            cts.Cancel();

            Assert.True(TransientRequestError.IsTransient(
                new HttpRequestException("Gegenstelle nicht erreichbar"), cts.Token));
        }
    }
}
