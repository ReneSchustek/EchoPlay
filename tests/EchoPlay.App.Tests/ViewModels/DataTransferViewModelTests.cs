using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Replication.Packaging;
using EchoPlay.Data.Replication;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="DataTransferViewModel"/>: Meldungen bei Erfolg und bei den erwartbaren
    /// Fehlern, Abbruch, Neustart nach dem Vormerken.
    /// </summary>
    public sealed class DataTransferViewModelTests
    {
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory Loggers =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        [Fact]
        public async Task Export_Success_ReportsFileAndSize()
        {
            ScriptedTransfer transfer = new() { ExportResult = (@"C:\Ziel\EchoPlay-Daten.echoplay", 3L * 1024 * 1024 * 1024) };
            DataTransferViewModel vm = Create(transfer);

            await vm.ExportAsync(@"C:\Ziel", credentialsPassword: null);

            Assert.Contains("EchoPlay-Daten.echoplay", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("GB", vm.StatusText, StringComparison.Ordinal);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Export_WithoutSpace_NamesBothNumbers()
        {
            ScriptedTransfer transfer = new() { ExportFailure = new InsufficientSpaceException(2L * 1024 * 1024 * 1024, 512L * 1024 * 1024) };
            DataTransferViewModel vm = Create(transfer);

            await vm.ExportAsync(@"C:\Ziel", credentialsPassword: null);

            Assert.Contains("2", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("512", vm.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Stage_RejectedPackage_ReturnsNull_AndSaysWhy()
        {
            ScriptedTransfer transfer = new() { StageFailure = new PackageRejectedException("Die Datei ist kein Paket.") };
            DataTransferViewModel vm = Create(transfer);

            StagedPackage? staged = await vm.StageAsync(@"C:\x.echoplay");

            Assert.Null(staged);
            Assert.Contains("kein Paket", vm.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Stage_Cancelled_SaysCancelled()
        {
            ScriptedTransfer transfer = new() { StageFailure = new OperationCanceledException() };
            DataTransferViewModel vm = Create(transfer);

            Assert.Null(await vm.StageAsync(@"C:\x.echoplay"));
            Assert.False(string.IsNullOrEmpty(vm.StatusText));
        }

        [Fact]
        public void Schedule_WrongPassword_DoesNotRestart()
        {
            ScriptedTransfer transfer = new() { ScheduleSucceeds = false };
            FakeProcessLauncher launcher = new();
            DataTransferViewModel vm = Create(transfer, launcher);

            Assert.False(vm.ScheduleAndRestart(Staged(), "falsch"));
            Assert.Empty(launcher.StartedPaths);
            Assert.False(string.IsNullOrEmpty(vm.StatusText));
        }

        [Fact]
        public void Schedule_RestartFails_AsksForAManualRestart()
        {
            FakeAppInstanceRegistration registration = new();
            DataTransferViewModel vm = Create(new ScriptedTransfer(), new FakeProcessLauncher(startSucceeds: false), registration);

            Assert.True(vm.ScheduleAndRestart(Staged(), null));
            Assert.Equal(["Release", "Reclaim"], registration.Calls);
            Assert.False(string.IsNullOrEmpty(vm.StatusText));
        }

        [Fact]
        public void Discard_ForgetsTheStagedPackage()
        {
            ScriptedTransfer transfer = new();
            DataTransferViewModel vm = Create(transfer);

            vm.Discard();

            Assert.True(transfer.Discarded);
        }

        [Fact]
        public async Task StateProperties_FollowTheOperation()
        {
            DataTransferViewModel vm = Create(new ScriptedTransfer());
            Assert.True(vm.IsNotBusy);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, vm.BusyVisibility);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, vm.StatusVisibility);

            vm.Cancel();
            await vm.ExportAsync(@"C:\Ziel", credentialsPassword: null);

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, vm.StatusVisibility);
            Assert.True(vm.Progress >= 0);
        }

        [Fact]
        public async Task HasCredentials_FollowsTheStore()
        {
            FakeSpotifyCredentialStore store = new();
            DataTransferViewModel vm = new(new ScriptedTransfer(), new ScriptedLibrary(), store, new FakeProcessLauncher(), new FakeAppInstanceRegistration(), Loggers);
            Assert.False(await vm.HasCredentialsAsync());

            await store.SaveAsync("id", "secret", TestContext.Current.CancellationToken);

            Assert.True(await vm.HasCredentialsAsync());
        }

        [Fact]
        public async Task PackLibrary_WithoutRoot_SaysThatTheRootIsMissing()
        {
            DataTransferViewModel vm = Create(new ScriptedTransfer(), library: new ScriptedLibrary { PackResult = null });

            await vm.PackLibraryAsync(@"C:\Ziel", credentialsPassword: null);

            Assert.Contains("Stammpfad", vm.StatusText, StringComparison.Ordinal);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task PackLibrary_Success_ReportsFileAndSize()
        {
            DataTransferViewModel vm = Create(new ScriptedTransfer());

            await vm.PackLibraryAsync(@"C:\Ziel", credentialsPassword: "Kennwort1");

            Assert.Contains("EchoPlay-Mediathek.echoplay-mediathek", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("MB", vm.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PackLibrary_TargetInsideTheLibrary_SaysWhy()
        {
            DataTransferViewModel vm = Create(new ScriptedTransfer(), library: new ScriptedLibrary
            {
                PackFailure = new ArgumentException("Das Archiv darf nicht in der Mediathek selbst liegen."),
            });

            await vm.PackLibraryAsync(@"C:\Ziel", credentialsPassword: null);

            Assert.Contains("nicht in der Mediathek", vm.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UnpackLibrary_Success_ReturnsTheUnpackedLibrary()
        {
            DataTransferViewModel vm = Create(new ScriptedTransfer());

            UnpackedLibrary? unpacked = await vm.UnpackLibraryAsync(@"C:\x.echoplay-mediathek", @"F:\Neu");

            Assert.NotNull(unpacked);
            Assert.Equal(@"F:\Neu", unpacked.LibraryRoot);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task UnpackLibrary_FolderNotEmpty_ReturnsNullAndSaysWhy()
        {
            DataTransferViewModel vm = Create(new ScriptedTransfer(), library: new ScriptedLibrary
            {
                UnpackFailure = new System.IO.IOException("Der Zielordner F:\\Neu ist nicht leer."),
            });

            Assert.Null(await vm.UnpackLibraryAsync(@"C:\x.echoplay-mediathek", @"F:\Neu"));
            Assert.Contains("nicht leer", vm.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public void DiscardUnpacked_KeepsTheFilesAndSaysWhere()
        {
            ScriptedTransfer transfer = new();
            DataTransferViewModel vm = Create(transfer);

            vm.DiscardUnpacked(new UnpackedLibrary(Staged(), @"F:\Neu", 2));

            Assert.True(transfer.Discarded);
            Assert.Contains(@"F:\Neu", vm.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public void Schedule_WithLibraryRoot_PassesItOn()
        {
            ScriptedTransfer transfer = new();
            DataTransferViewModel vm = Create(transfer);

            Assert.True(vm.ScheduleAndRestart(Staged(), null, @"F:\Neu"));
            Assert.Equal(@"F:\Neu", transfer.ScheduledRoot);
        }

        private static DataTransferViewModel Create(
            ScriptedTransfer transfer, FakeProcessLauncher? launcher = null, FakeAppInstanceRegistration? registration = null, ScriptedLibrary? library = null) =>
            new(transfer, library ?? new ScriptedLibrary(), new FakeSpotifyCredentialStore(), launcher ?? new FakeProcessLauncher(startSucceeds: false), registration ?? new FakeAppInstanceRegistration(), Loggers);

        private static StagedPackage Staged() => new(
            new PackageManifest(1, "1.5.3", "20261120130408_AddChangeStamp", Guid.NewGuid(), DateTimeOffset.UtcNow, null,
                [new PackageEntry(PackageFormat.DatabaseEntry, 1, new string('a', 64))]),
            @"C:\staged", ReadOnlyMemory<byte>.Empty, new ImportPreview(1, 2, 3, 4, 5, 6));

        /// <summary>Ein Übertragungsdienst, der vorgegebene Ergebnisse liefert oder vorgegebene Fehler wirft.</summary>
        private sealed class ScriptedTransfer : IDataTransferService
        {
            public (string Path, long Length) ExportResult { get; init; } = (@"C:\x.echoplay", 1);

            public Exception? ExportFailure { get; init; }

            public Exception? StageFailure { get; init; }

            public bool ScheduleSucceeds { get; init; } = true;

            public bool Discarded { get; private set; }

            public Task<(string Path, long Length)> ExportAsync(string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                progress?.Report(0.5);
                return ExportFailure is null ? Task.FromResult(ExportResult) : Task.FromException<(string, long)>(ExportFailure);
            }

            public Task<StagedPackage> StageAsync(string packagePath, CancellationToken cancellationToken) =>
                StageFailure is null ? Task.FromResult(Staged()) : Task.FromException<StagedPackage>(StageFailure);

            public string? ScheduledRoot { get; private set; }

            public bool ScheduleImport(StagedPackage staged, string? credentialsPassword, string? libraryRoot = null)
            {
                ScheduledRoot = libraryRoot;
                return ScheduleSucceeds;
            }

            public void DiscardImport() => Discarded = true;
        }

        /// <summary>Packen und Auspacken als Attrappe mit vorgegebenen Ergebnissen.</summary>
        private sealed class ScriptedLibrary : ILibraryTransferService
        {
            public (string Path, long Length)? PackResult { get; init; } = (@"C:\Ziel\EchoPlay-Mediathek.echoplay-mediathek", 5L * 1024 * 1024);

            public Exception? PackFailure { get; init; }

            public Exception? UnpackFailure { get; init; }

            public Task<(string Path, long Length)?> PackAsync(string folder, string? credentialsPassword, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                progress?.Report(0.5);
                return PackFailure is null ? Task.FromResult(PackResult) : Task.FromException<(string, long)?>(PackFailure);
            }

            public Task<UnpackedLibrary> UnpackAsync(string archivePath, string targetRoot, IProgress<double>? progress, CancellationToken cancellationToken) =>
                UnpackFailure is null
                    ? Task.FromResult(new UnpackedLibrary(Staged(), targetRoot, 3))
                    : Task.FromException<UnpackedLibrary>(UnpackFailure);
        }
    }
}
