using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using System;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="ScanEventService"/> — thread-sicherer Scan-Flag und
    /// das <see cref="ScanEventService.SeriesSynced"/>-Event.
    /// </summary>
    public sealed class ScanEventServiceTests
    {
        [Fact]
        public void IsScanRunning_IsFalse_ByDefault()
        {
            ScanEventService service = new();

            Assert.False(service.IsScanRunning);
        }

        [Fact]
        public void BeginScan_SetsRunningToTrue_EndScanResetsToFalse()
        {
            ScanEventService service = new();

            service.BeginScan();
            Assert.True(service.IsScanRunning);

            service.EndScan();
            Assert.False(service.IsScanRunning);
        }

        [Fact]
        public void BeginScan_IsIdempotent()
        {
            ScanEventService service = new();

            service.BeginScan();
            service.BeginScan();
            Assert.True(service.IsScanRunning);

            service.EndScan();
            Assert.False(service.IsScanRunning);
        }

        [Fact]
        public void SeriesSyncedEvent_IsDispatchedToSubscribers()
        {
            ScanEventService service = new();
            Series? captured = null;
            service.SeriesSynced += s => captured = s;

            Series series = new() { Title = "Test-Serie" };
            service.RaiseSeriesSynced(series);

            Assert.NotNull(captured);
            Assert.Same(series, captured);
        }

        [Fact]
        public void SeriesSyncedEvent_WithoutSubscribers_DoesNotThrow()
        {
            ScanEventService service = new();

            Exception? exception = Record.Exception(() => service.RaiseSeriesSynced(new Series { Title = "Titel" }));

            Assert.Null(exception);
        }
    }
}
