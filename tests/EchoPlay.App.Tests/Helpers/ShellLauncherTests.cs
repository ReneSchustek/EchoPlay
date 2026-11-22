using EchoPlay.App.Helpers;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Prüft, dass ein gescheiterter Start über die Shell als <see langword="false"/> samt Grund
    /// zurückkommt statt als Ausnahme.
    /// </summary>
    /// <remarks>
    /// Nur Fehlerwege: Ein Test darf nie wirklich einen Prozess starten. Eine Datei, die es nicht
    /// gibt, lässt die Shell scheitern, bevor irgendetwas läuft.
    /// </remarks>
    public sealed class ShellLauncherTests
    {
        [Fact]
        public void TryStart_WithAMissingFile_ReportsFailureWithReason()
        {
            string missing = Path.Combine(Path.GetTempPath(), "EchoPlay-" + Guid.NewGuid().ToString("N"), "fehlt.exe");

            bool started = ShellLauncher.TryStart(missing, out string? failureReason);

            Assert.False(started);
            Assert.False(string.IsNullOrWhiteSpace(failureReason));
        }

        [Fact]
        public void TryStart_WithAnEmptyTarget_ReportsFailureWithReason()
        {
            bool started = ShellLauncher.TryStart(string.Empty, out string? failureReason);

            Assert.False(started);
            Assert.False(string.IsNullOrWhiteSpace(failureReason));
        }
    }
}
