using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Models;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für den App-Dienst der ausgeblendeten Dialoge. Sein Kern ist der
    /// Zwischenspeicher: Ohne ihn kostete jede Rückfrage und jeder Seitenwechsel eine
    /// Datenbankrunde.
    /// </summary>
    public sealed class DialogSuppressionServiceTests
    {
        private static (DialogSuppressionService Service, FakeDialogSuppressionDataService Data) Build()
        {
            FakeDialogSuppressionDataService data = new();
            ServiceCollection services = new();
            _ = services.AddScoped<IDialogSuppressionDataService>(_ => data);
            ServiceProvider provider = services.BuildServiceProvider();

            DialogSuppressionService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLoggerFactory());

            return (service, data);
        }

        [Fact]
        public async Task IsSuppressedAsync_OhneEintrag_LiefertFalse()
        {
            (DialogSuppressionService service, _) = Build();

            Assert.False(await service.IsSuppressedAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task IsSuppressedAsync_None_FragtDieDatenbankNichtEinmal()
        {
            (DialogSuppressionService service, FakeDialogSuppressionDataService data) = Build();

            Assert.False(await service.IsSuppressedAsync(DialogKey.None, TestContext.Current.CancellationToken));
            Assert.Equal(0, data.GetAllCallCount);
        }

        [Fact]
        public async Task SuppressAsync_WirktSofortUndUeberlebtDieNaechsteAbfrage()
        {
            (DialogSuppressionService service, _) = Build();

            await service.SuppressAsync(DialogKey.LibraryReinit, TestContext.Current.CancellationToken);

            Assert.True(await service.IsSuppressedAsync(DialogKey.LibraryReinit, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ZweiAbfragen_LesenNurEinmalAusDerDatenbank()
        {
            (DialogSuppressionService service, FakeDialogSuppressionDataService data) = Build();

            _ = await service.IsSuppressedAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken);
            _ = await service.IsSuppressedAsync(DialogKey.LibraryReinit, TestContext.Current.CancellationToken);

            Assert.Equal(1, data.GetAllCallCount);
        }

        [Fact]
        public async Task RestoreAsync_NimmtDenEintragAuchAusDemZwischenspeicher()
        {
            (DialogSuppressionService service, _) = Build();
            await service.SuppressAsync(DialogKey.OnlineSearchFailed, TestContext.Current.CancellationToken);
            _ = await service.IsSuppressedAsync(DialogKey.OnlineSearchFailed, TestContext.Current.CancellationToken);

            await service.RestoreAsync(DialogKey.OnlineSearchFailed, TestContext.Current.CancellationToken);

            Assert.False(await service.IsSuppressedAsync(DialogKey.OnlineSearchFailed, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task RestoreAllAsync_LeertDenZwischenspeicher()
        {
            (DialogSuppressionService service, _) = Build();
            await service.SuppressAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken);
            await service.SuppressAsync(DialogKey.LibraryReinit, TestContext.Current.CancellationToken);
            _ = await service.IsSuppressedAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken);

            await service.RestoreAllAsync(TestContext.Current.CancellationToken);

            Assert.False(await service.IsSuppressedAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken));
            Assert.False(await service.IsSuppressedAsync(DialogKey.LibraryReinit, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task GetSuppressedAsync_UeberspringtUnbekannteSchluessel()
        {
            (DialogSuppressionService service, FakeDialogSuppressionDataService data) = Build();
            await data.SuppressAsync("DiesenDialogGibtEsNichtMehr", TestContext.Current.CancellationToken);
            await data.SuppressAsync(DialogKey.ImportFailed.ToString(), TestContext.Current.CancellationToken);

            IReadOnlyList<SuppressedDialogInfo> suppressed =
                await service.GetSuppressedAsync(TestContext.Current.CancellationToken);

            SuppressedDialogInfo only = Assert.Single(suppressed);
            Assert.Equal(DialogKey.ImportFailed, only.Key);
        }

        [Fact]
        public async Task NichtLesbareTabelle_ZeigtDenDialogUndVersuchtEsErneut()
        {
            // Vor der ersten Migration oder bei gesperrter Datenbank ist die Tabelle nicht
            // lesbar. Ein nicht lesbarer Merkzettel darf keinen Hinweis verschlucken — und
            // das Ergebnis darf auch nicht zwischengespeichert werden.
            (DialogSuppressionService service, FakeDialogSuppressionDataService data) = Build();
            data.ReadFailure = new InvalidOperationException("no such table: DialogSuppressions");

            Assert.False(await service.IsSuppressedAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken));

            data.ReadFailure = null;
            await data.SuppressAsync(DialogKey.ImportFailed.ToString(), TestContext.Current.CancellationToken);

            Assert.True(await service.IsSuppressedAsync(DialogKey.ImportFailed, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void Constructor_OhneBereichsfabrik_Wirft()
        {
            _ = Assert.Throws<ArgumentNullException>(
                () => new DialogSuppressionService(null!, new FakeLoggerFactory()));
        }
    }
}
