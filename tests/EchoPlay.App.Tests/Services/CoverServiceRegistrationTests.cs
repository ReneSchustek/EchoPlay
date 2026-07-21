using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Sichert die DI-Registrierung des CoverService ab. Regression zu einem Startup-Hänger:
    /// Die <see cref="ICoverService"/>-Factory löste sich selbst auf
    /// (<c>GetRequiredService&lt;ICoverService&gt;</c> statt <c>&lt;CoverService&gt;</c>),
    /// wodurch der DI-Container beim Auflösen von <see cref="IStartupValidator"/> endlos
    /// rekursierte und der App-Start (weißer Splash) einfror. Der Container erkennt nur
    /// Konstruktor-, keine Factory-Zyklen — deshalb braucht es diese explizite Absicherung.
    /// </summary>
    public sealed class CoverServiceRegistrationTests
    {
        [Fact]
        public async Task RegisterCoverService_ICoverService_LoestKonkretenCoverServiceAuf_OhneEndlosRekursion()
        {
            ServiceCollection services = new();
            // CoverService-Konstruktor braucht ILoggerFactory; IServiceScopeFactory stellt der Container selbst bereit.
            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());

            // Die ECHTE Registrierungsmethode der App aufrufen — so schützt der Test die
            // reale Verdrahtung, nicht nur ein nachgebautes Muster.
            EchoPlay.App.App.RegisterCoverService(services);

            await using ServiceProvider provider = services.BuildServiceProvider();

            // Auflösung in einem Worker: Eine (regressierte) Selbst-Auflösung würde endlos
            // rekursieren. Der WaitAsync-Timeout macht daraus einen sauberen Fehlschlag mit
            // klarer Meldung statt eines hängenden Testlaufs.
            Task<(CoverService Concrete, ICoverService ViaInterface)> resolve = Task.Run(
                () =>
                {
                    CoverService concrete = provider.GetRequiredService<CoverService>();
                    ICoverService viaInterface = provider.GetRequiredService<ICoverService>();
                    return (concrete, viaInterface);
                },
                TestContext.Current.CancellationToken);

            try
            {
                (CoverService concrete, ICoverService viaInterface) =
                    await resolve.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

                Assert.NotNull(viaInterface);
                Assert.Same(concrete, viaInterface);
            }
            catch (TimeoutException)
            {
                Assert.Fail(
                    "ICoverService konnte nicht innerhalb von 10 s aufgelöst werden — vermutlich Selbst-Auflösung / DI-Endlos-Rekursion.");
            }
        }
    }
}
