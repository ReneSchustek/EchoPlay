using EchoPlay.Logger.Abstractions;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Verifiziert die Default-Implementierung der Message-Template-Overloads in <see cref="ILogger"/>.
    /// Strukturierte Sinks koennen die Methoden ueberschreiben — der Default-Pfad rendert per
    /// String-Substitution und delegiert an den Plain-Message-Pfad.
    /// </summary>
    public sealed class TemplateOverloadTests
    {
        [Fact]
        public void Info_Template_RendersPlaceholdersInOrder()
        {
            CapturingTestLogger captured = new();
            ILogger logger = captured;

            logger.Info("User {UserId} hat Serie {SeriesId} importiert", "U-42", "S-007");

            Assert.Equal("User U-42 hat Serie S-007 importiert", captured.LastMessage);
        }

        [Fact]
        public void Warning_Template_RendersPlaceholders()
        {
            CapturingTestLogger captured = new();
            ILogger logger = captured;

            logger.Warning("Pfad {Path} nicht erreichbar", "C:\\foo");

            Assert.Equal("Pfad C:\\foo nicht erreichbar", captured.LastMessage);
        }

        [Fact]
        public void Error_TemplateWithException_PassesExceptionThrough()
        {
            CapturingTestLogger captured = new();
            ILogger logger = captured;
            InvalidOperationException ex = new("Test");

            logger.Error("Operation {Op} fehlgeschlagen", ex, "Save");

            Assert.Equal("Operation Save fehlgeschlagen", captured.LastMessage);
            Assert.Same(ex, captured.LastException);
        }

        [Fact]
        public void Debug_TemplateWhenDisabled_DoesNotRender()
        {
            CapturingTestLogger captured = new() { IsDebugEnabled = false };
            ILogger logger = captured;

            logger.Debug("Detail {Value}", "should-not-appear");

            Assert.Null(captured.LastMessage);
        }

        [Fact]
        public void Debug_TemplateWhenEnabled_Renders()
        {
            CapturingTestLogger captured = new() { IsDebugEnabled = true };
            ILogger logger = captured;

            logger.Debug("Detail {Value}", 42);

            Assert.Equal("Detail 42", captured.LastMessage);
        }

        [Fact]
        public void Info_TemplateWithoutArgs_LeavesTemplateUntouched()
        {
            CapturingTestLogger captured = new();
            ILogger logger = captured;

            logger.Info("Statisch ohne Platzhalter");

            Assert.Equal("Statisch ohne Platzhalter", captured.LastMessage);
        }

        [Fact]
        public void Info_TemplateWithNullArg_RendersNullPlaceholder()
        {
            CapturingTestLogger captured = new();
            ILogger logger = captured;

            logger.Info("Wert {Value}", new object?[] { null });

            Assert.Equal("Wert (null)", captured.LastMessage);
        }

        // Test-Logger ohne Sinks; nur fuer Default-Method-Verifikation.
        private sealed class CapturingTestLogger : ILogger
        {
            public bool IsDebugEnabled { get; set; } = true;
            public string? LastMessage { get; private set; }
            public Exception? LastException { get; private set; }

            public void Trace(string message) => LastMessage = message;
            public void Debug(string message) => LastMessage = message;
            public void Info(string message) => LastMessage = message;
            public void Warning(string message) => LastMessage = message;
            public void Error(string message, Exception? exception = null)
            {
                LastMessage = message;
                LastException = exception;
            }
            public void Fatal(string message, Exception? exception = null)
            {
                LastMessage = message;
                LastException = exception;
            }
            public EchoPlay.Logger.Scoping.LogScope BeginScope(string name) => new(name);
        }
    }
}
