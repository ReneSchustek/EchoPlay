using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.TagManager.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Baut den Kontext der Tag-Aktionen für die Tests der einzelnen Aktionsklassen. Der
    /// Abfrage-Koordinator ist echt und löst den Abfragedienst über einen Bereich auf, wie in der
    /// App; nur der Abfragedienst selbst ist ein Nachbau.
    /// </summary>
    internal static class TagActionsTestContext
    {
        /// <summary>
        /// Erzeugt den Kontext. Ohne Angabe kommen ein leerer Abfragedienst, ein Umbenenn-Nachbau
        /// ohne Vorschau und eine Netzsperre, die den Zugriff erlaubt.
        /// </summary>
        public static TagManagerActionsContext Create(
            ITagService tagService,
            FakeErrorDialogService errorDialog,
            FakeConfirmationDialogService confirmation,
            ITagLookupService? lookupService = null,
            IFileRenameService? renameService = null,
            IOnlineAccessGuard? onlineGuard = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITagLookupService>(_ => lookupService ?? new FakeTagLookupService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new TagManagerActionsContext(
                TagService: tagService,
                LookupCoordinator: new TagLookupCoordinator(
                    provider.GetRequiredService<IServiceScopeFactory>(), new FakeLoggerFactory()),
                FileRenameService: renameService ?? new FakeFileRenameService(),
                ErrorDialogService: errorDialog,
                ConfirmationDialogService: confirmation,
                OnlineAccessGuard: onlineGuard ?? new FakeOnlineAccessGuard());
        }

        /// <summary>Dateiliste mit den angegebenen Dateien eines Ordners, keine ausgewählt.</summary>
        public static TagFileListViewModel FileList(string folderPath, params string[] files)
        {
            TagFileListViewModel fileList = new();
            fileList.SetFiles([.. files.Select(file => new TagFileItemViewModel(file, folderPath))], folderPath);
            return fileList;
        }
    }
}
