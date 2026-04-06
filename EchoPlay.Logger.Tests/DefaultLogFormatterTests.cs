using EchoPlay.Logger.Formatting;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="DefaultLogFormatter"/>.
    /// Prüft das Ausgabeformat für alle relevanten Eintragsvarianten.
    /// </summary>
    public sealed class DefaultLogFormatterTests
    {
        /// <summary>Fester Zeitstempel für deterministische Vergleiche.</summary>
        private static readonly DateTime TestZeitstempel = new(2026, 3, 2, 14, 30, 45, 123);

        /// <summary>
        /// Format ohne Scopes und ohne Exception entspricht dem exakten Grundformat.
        /// </summary>
        [Fact]
        public void Format_OhneScopes_OhneException_ExaktesGrundformat()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Debug,
                Message: "Testmeldung",
                Category: "TestKlasse",
                Scopes: []);

            string result = formatter.Format(entry);

            Assert.Equal("2026-03-02 14:30:45.123 [Debug] [TestKlasse]: Testmeldung", result);
        }

        /// <summary>
        /// Format enthält den Zeitstempel im Format yyyy-MM-dd HH:mm:ss.fff.
        /// </summary>
        [Fact]
        public void Format_EnthältZeitstempelInKorrektemFormat()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: new DateTime(2026, 1, 7, 9, 5, 3, 7),
                Level: LogLevel.Information,
                Message: "Meldung",
                Category: "Klasse",
                Scopes: []);

            string result = formatter.Format(entry);

            Assert.Contains("2026-01-07 09:05:03.007", result);
        }

        /// <summary>
        /// Format enthält den Level-Text in eckigen Klammern.
        /// </summary>
        [Fact]
        public void Format_EnthältLevel()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Warning,
                Message: "Meldung",
                Category: "Klasse",
                Scopes: []);

            string result = formatter.Format(entry);

            Assert.Contains("[Warning]", result);
        }

        /// <summary>
        /// Format enthält Kategorie in eckigen Klammern und die Nachricht.
        /// </summary>
        [Fact]
        public void Format_EnthältKategorieUndNachricht()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Information,
                Message: "Die eigentliche Meldung",
                Category: "MeineKlasse",
                Scopes: []);

            string result = formatter.Format(entry);

            Assert.Contains("[MeineKlasse]", result);
            Assert.Contains("Die eigentliche Meldung", result);
        }

        /// <summary>
        /// Bei einem Eintrag ohne Scopes enthält das Format keinen Scope-Block.
        /// </summary>
        [Fact]
        public void Format_OhneScopes_KeinScopeBlock()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Information,
                Message: "Meldung",
                Category: "Klasse",
                Scopes: []);

            string result = formatter.Format(entry);

            // Kein Pfeil-Trennzeichen, das auf Scopes hinweist
            Assert.DoesNotContain(" →", result);
        }

        /// <summary>
        /// Bei einem Eintrag mit einem Scope erscheint dieser in eckigen Klammern nach der Kategorie.
        /// </summary>
        [Fact]
        public void Format_MitEinemScope_ScopeInEckigenKlammern()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Information,
                Message: "Meldung",
                Category: "Klasse",
                Scopes: ["API:Test:Vorgang"]);

            string result = formatter.Format(entry);

            Assert.Contains("[API:Test:Vorgang]", result);
        }

        /// <summary>
        /// Mehrere Scopes werden mit einem Pfeil als Trennzeichen dargestellt.
        /// </summary>
        [Fact]
        public void Format_MitMehrerenScopes_ScopesMitPfeilGetrennt()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Information,
                Message: "Meldung",
                Category: "Klasse",
                Scopes: ["Erster", "Zweiter", "Dritter"]);

            string result = formatter.Format(entry);

            Assert.Contains("Erster → Zweiter → Dritter", result);
        }

        /// <summary>
        /// Bei einem Eintrag ohne Exception erscheint kein Exception-Block im Format.
        /// </summary>
        [Fact]
        public void Format_OhneException_KeinExceptionBlock()
        {
            DefaultLogFormatter formatter = new();
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Information,
                Message: "Meldung",
                Category: "Klasse",
                Scopes: []);

            string result = formatter.Format(entry);

            Assert.DoesNotContain("Exception", result);
        }

        /// <summary>
        /// Bei einem Eintrag mit Exception enthält das Format den Exception-Block
        /// mit der Fehlermeldung.
        /// </summary>
        [Fact]
        public void Format_MitException_EnthältExceptionUndMeldung()
        {
            DefaultLogFormatter formatter = new();
            InvalidOperationException exception = new("Testfehler-Ursache");
            LogEntry entry = new(
                Timestamp: TestZeitstempel,
                Level: LogLevel.Error,
                Message: "Fehlermeldung",
                Category: "Klasse",
                Scopes: [],
                Exception: exception);

            string result = formatter.Format(entry);

            Assert.Contains("Exception:", result);
            Assert.Contains("Testfehler-Ursache", result);
        }
    }
}
