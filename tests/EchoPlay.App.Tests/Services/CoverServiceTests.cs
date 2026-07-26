using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AppCoverService = EchoPlay.App.Services.CoverService;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="AppCoverService"/> – Schwerpunkt auf dem Diagnose-Schutzgitter
    /// aus Arbeitspaket 430: Ein endgültig fehlgeschlagener Cover-DB-Write darf nicht mehr still
    /// verschluckt werden, sondern wird als WARN sichtbar und wirft nicht mehr nach außen.
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

        private sealed class ThrowingCoverImageDataService : ICoverImageDataService
        {
            public Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default) =>
                throw new DbUpdateException("Simulierter dauerhafter DB-Fehler.");

            public Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<Guid>> GetUncheckedEntityIdsAsync(string entityType, DateTime cooldownThreshold, int limit, CancellationToken cancellationToken = default) =>
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

        private sealed class CapturingLoggerFactory(ILogger logger) : ILoggerFactory
        {
            public ILogger CreateLogger(string category) => logger;
        }

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
