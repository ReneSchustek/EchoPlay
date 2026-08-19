using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using System;
using System.Collections.Generic;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Prüft die verzögerte Debug-Meldung, ein Muster ohne Werte und das Entfernen
    /// eines Bereichs, den es nicht gibt.
    /// </summary>
    /// <remarks>
    /// Die verzögerte Form ist der Grund, warum Debug-Meldungen in heißen Pfaden stehen
    /// dürfen: Ist die Stufe aus, wird die Zeichenkette nie gebaut. Wäre es umgekehrt,
    /// zahlte jeder Durchlauf für Meldungen, die niemand liest.
    /// </remarks>
    public sealed class LazyDebugAndEmptyTemplateTests
    {
        [Fact]
        public void Debug_WithTheLevelSwitchedOff_DoesNotBuildTheMessage()
        {
            RecordingLogger recording = new(debugEnabled: false);
            ILogger logger = recording;
            bool built = false;

            logger.Debug(() => { built = true; return "teuer gebaut"; });

            Assert.False(built);
            Assert.Empty(recording.Messages);
        }

        [Fact]
        public void Debug_WithTheLevelSwitchedOn_BuildsAndWritesTheMessage()
        {
            RecordingLogger recording = new(debugEnabled: true);
            ILogger logger = recording;

            logger.Debug(() => "teuer gebaut");

            Assert.Equal("teuer gebaut", Assert.Single(recording.Messages));
        }

        [Fact]
        public void Debug_WithoutAMessageFactory_Throws()
        {
            ILogger logger = new RecordingLogger(debugEnabled: true);

            _ = Assert.Throws<ArgumentNullException>(() => logger.Debug((Func<string>)null!));
        }

        [Fact]
        public void Info_WithATemplateButNoValues_KeepsThePlaceholderAsIs()
        {
            RecordingLogger recording = new(debugEnabled: false);
            ILogger logger = recording;

            logger.Info("Serie {SeriesId} importiert");

            // Ohne Werte gibt es nichts einzusetzen. Der Platzhalter bleibt sichtbar,
            // statt dass die Meldung verstümmelt im Protokoll landet.
            Assert.Equal("Serie {SeriesId} importiert", Assert.Single(recording.Messages));
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        /// <summary>Protokollkanal, der jede Meldung mitschreibt.</summary>
        private sealed class RecordingLogger : ILogger
        {
            public RecordingLogger(bool debugEnabled) => IsDebugEnabled = debugEnabled;

            public bool IsDebugEnabled { get; }

            public List<string> Messages { get; } = [];

            public void Trace(string message) => Messages.Add(message);

            public void Debug(string message) => Messages.Add(message);

            public void Info(string message) => Messages.Add(message);

            public void Warning(string message) => Messages.Add(message);

            public void Error(string message, Exception? exception = null) => Messages.Add(message);

            public void Fatal(string message, Exception? exception = null) => Messages.Add(message);

            public LogScope BeginScope(string name) => new(name);
        }
    }
}
