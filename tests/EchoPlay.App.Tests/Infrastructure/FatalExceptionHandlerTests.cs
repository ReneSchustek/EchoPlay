using EchoPlay.App.Infrastructure;
using EchoPlay.App.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Deckt den Fatal-Log-Pfad für globale Exception-Hooks ab
    /// (<see cref="AppDomain.UnhandledException"/> und <see cref="TaskScheduler.UnobservedTaskException"/>).
    /// Der Pfad mit <see langword="null"/>-Logger (EmergencyTrace-Fallback) wird ebenfalls geprueft,
    /// ohne <c>Trace.Listeners</c> zu koppeln — Kriterium ist, dass kein Crash auftritt.
    /// </summary>
    public sealed class FatalExceptionHandlerTests
    {
        [Fact]
        public void HandleDomainException_LoggerSet_LogsFatalWithTerminatingMarker()
        {
            CapturingLogger logger = new();
            InvalidOperationException exception = new("boom");
            UnhandledExceptionEventArgs args = new(exception, isTerminating: true);

            FatalExceptionHandler.HandleDomainException(logger, args);

            _ = Assert.Single(logger.Entries);
            (string level, string message, Exception? ex) = logger.Entries[0];
            Assert.Equal("Fatal", level);
            Assert.Contains("TERMINATING", message, StringComparison.Ordinal);
            Assert.Contains("boom", message, StringComparison.Ordinal);
            Assert.Same(exception, ex);
        }

        [Fact]
        public void HandleDomainException_NonTerminating_UsesNonTerminatingMarker()
        {
            CapturingLogger logger = new();
            InvalidOperationException exception = new("soft");
            UnhandledExceptionEventArgs args = new(exception, isTerminating: false);

            FatalExceptionHandler.HandleDomainException(logger, args);

            Assert.Contains("NON-TERMINATING", logger.Entries.Single().Message, StringComparison.Ordinal);
        }

        [Fact]
        public void HandleDomainException_NullLogger_FallsBackWithoutThrowing()
        {
            UnhandledExceptionEventArgs args = new(new InvalidOperationException("any"), isTerminating: false);

            Exception? recorded = Record.Exception(() =>
                FatalExceptionHandler.HandleDomainException(logger: null, args));

            Assert.Null(recorded);
        }

        [Fact]
        public void HandleDomainException_NullArgs_ThrowsArgumentNullException()
        {
            CapturingLogger logger = new();

            _ = Assert.Throws<ArgumentNullException>(() =>
                FatalExceptionHandler.HandleDomainException(logger, e: null!));
        }

        [Fact]
        public void HandleUnobservedTaskException_LoggerSet_LogsErrorAndMarksObserved()
        {
            CapturingLogger logger = new();
            AggregateException aggregate = new(new InvalidOperationException("unobserved"));
            UnobservedTaskExceptionEventArgs args = new(aggregate);

            FatalExceptionHandler.HandleUnobservedTaskException(logger, args);

            _ = Assert.Single(logger.Entries);
            Assert.Equal("Error", logger.Entries[0].Level);
            Assert.Contains("UnobservedTaskException", logger.Entries[0].Message, StringComparison.Ordinal);
            Assert.Contains("unobserved", logger.Entries[0].Message, StringComparison.Ordinal);
            Assert.True(IsObserved(args), "SetObserved muss aufgerufen werden, sonst wuerde der Prozess beim GC abgebrochen.");
        }

        [Fact]
        public void HandleUnobservedTaskException_NullLogger_StillMarksObserved()
        {
            AggregateException aggregate = new(new InvalidOperationException("any"));
            UnobservedTaskExceptionEventArgs args = new(aggregate);

            FatalExceptionHandler.HandleUnobservedTaskException(logger: null, args);

            Assert.True(IsObserved(args));
        }

        [Fact]
        public void HandleUnobservedTaskException_TaskCanceled_LogsAsInfo()
        {
            // Beim App-Shutdown werden DI-Scopes disposed, pending DB-/HTTP-Tasks
            // canceln still. Die Tasks sind dann unbeobachtet — der Handler muss
            // erkennen, dass das erwartetes Verhalten ist, und nicht als Error loggen.
            CapturingLogger logger = new();
            AggregateException aggregate = new(new TaskCanceledException("shutdown"));
            UnobservedTaskExceptionEventArgs args = new(aggregate);

            FatalExceptionHandler.HandleUnobservedTaskException(logger, args);

            _ = Assert.Single(logger.Entries);
            Assert.Equal("Info", logger.Entries[0].Level);
            Assert.Contains("Shutdown-Cancellation", logger.Entries[0].Message, StringComparison.Ordinal);
            Assert.True(IsObserved(args));
        }

        [Fact]
        public void HandleUnobservedTaskException_NullArgs_ThrowsArgumentNullException()
        {
            CapturingLogger logger = new();

            _ = Assert.Throws<ArgumentNullException>(() =>
                FatalExceptionHandler.HandleUnobservedTaskException(logger, e: null!));
        }

        /// <summary>
        /// <see cref="UnobservedTaskExceptionEventArgs"/> hat keinen oeffentlichen Observed-Getter,
        /// aber Reflection zeigt das interne Feld. Alternative waere ein Provoke + GC-Kollektion,
        /// das ist im Test unzuverlaessig.
        /// </summary>
        private static bool IsObserved(UnobservedTaskExceptionEventArgs args)
        {
            System.Reflection.FieldInfo? field = typeof(UnobservedTaskExceptionEventArgs)
                .GetField("m_observed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return field is not null && (bool)field.GetValue(args)!;
        }
    }
}
