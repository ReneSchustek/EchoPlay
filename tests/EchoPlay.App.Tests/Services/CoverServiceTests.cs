using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AppCoverService = EchoPlay.App.Services.CoverService;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="AppCoverService"/> mit Schwerpunkt auf dem Diagnose-Schutzgitter:
    /// Ein endgültig fehlgeschlagener Cover-DB-Write darf nicht still
    /// verschluckt werden, sondern wird als WARN sichtbar und wirft nicht nach außen.
    /// </summary>
    public sealed class CoverServiceTests
    {
        [Fact]
        public async Task SetSeriesCoverAsync_WriteFailsAllRetries_LogsFinalWarningAndSwallows()
        {
            CapturingLogger logger = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ICoverImageDataService>(_ => new ThrowingCoverImageDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            AppCoverService sut = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new CapturingLoggerFactory(logger));

            // Kontrollierte Degradation: darf trotz dauerhaftem DB-Fehler NICHT werfen.
            await sut.SetSeriesCoverAsync(TestIds.SeriesA, [1, 2, 3], cancellationToken: TestContext.Current.CancellationToken);

            // 2 Retry-WARNs (Versuch 1 + 2) + 1 finaler WARN nach dem 3. Versuch.
            Assert.Equal(3, logger.Warnings.Count);
            Assert.Contains(logger.Warnings, w => w.Contains("endgültig fehlgeschlagen", StringComparison.Ordinal));
        }

        /// <summary>
        /// Scheitert beim Speichern dauerhaft mit einem Datenbankfehler; alle anderen Methoden sind
        /// hier nicht vorgesehen.
        /// </summary>
        private sealed class ThrowingCoverImageDataService : ICoverImageDataService
        {
            public Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, Uri? sourceUrl = null, CancellationToken cancellationToken = default) =>
                throw new DbUpdateException("Simulierter dauerhafter DB-Fehler.");

            public Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<bool> ExistsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<int> ClearAllAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<int> DeleteByEntitiesAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }

        /// <summary>
        /// Gibt für jede Kategorie denselben Logger aus.
        /// </summary>
        private sealed class CapturingLoggerFactory(ILogger logger) : ILoggerFactory
        {
            public ILogger CreateLogger(string category) => logger;
        }

        /// <summary>
        /// Sammelt nur Warnungen; die übrigen Stufen fallen weg.
        /// </summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<string> Warnings { get; } = [];

            public bool IsDebugEnabled => false;
            public void Trace(string message) { }
            public void Debug(string message) { }
            public void Info(string message) { }
            public void Warning(string message) => Warnings.Add(message);
            public void Error(string message, Exception? exception = null) { }
            public void Fatal(string message, Exception? exception = null) { }
            public LogScope BeginScope(string name) => new(name);
        }
    }
}
