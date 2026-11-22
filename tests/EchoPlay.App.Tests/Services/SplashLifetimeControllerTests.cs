using EchoPlay.App.Services;
using Microsoft.Extensions.Time.Testing;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Verifiziert, dass die Mindest-Anzeigedauer des Splash auch bei warmem Cache eingehalten wird.
    /// Die Uhr wird vorgestellt statt abgewartet; so prüfen die Tests die Grenze genau und ohne
    /// anderthalb Sekunden Echtzeit.
    /// </summary>
    public sealed class SplashLifetimeControllerTests
    {
        [Fact]
        public void WaitForMinimumDurationAsync_ElapsedAlreadyExceedsMinimum_ReturnsImmediately()
        {
            FakeTimeProvider clock = new();
            SplashLifetimeController controller = new(clock);
            clock.Advance(SplashLifetimeController.MinimumDuration + TimeSpan.FromMilliseconds(50));

            Task wait = controller.WaitForMinimumDurationAsync(TestContext.Current.CancellationToken);

            Assert.True(wait.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task WaitForMinimumDurationAsync_FreshController_WaitsExactlyTheMinimum()
        {
            FakeTimeProvider clock = new();
            SplashLifetimeController controller = new(clock);

            Task wait = controller.WaitForMinimumDurationAsync(TestContext.Current.CancellationToken);
            clock.Advance(SplashLifetimeController.MinimumDuration - TimeSpan.FromMilliseconds(1));
            Assert.False(wait.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await wait;
        }

        [Fact]
        public async Task WaitForMinimumDurationAsync_TokenCanceled_ReturnsEarlyWithoutThrowing()
        {
            FakeTimeProvider clock = new();
            SplashLifetimeController controller = new(clock);
            using CancellationTokenSource cts = new();

            Task wait = controller.WaitForMinimumDurationAsync(cts.Token);
            Assert.False(wait.IsCompleted);

            // Der Splash wird geschlossen, bevor die Mindestzeit um ist: Das Warten endet
            // sofort und still, die Uhr steht dabei noch am Anfang.
            await cts.CancelAsync();
            await wait;

            Assert.True(wait.IsCompletedSuccessfully);
        }
    }
}
