using EchoPlay.App.Services;
using EchoPlay.TagManager.Abstractions;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Bündelt die per DI aufgelösten Service-Abhängigkeiten für <see cref="TagManagerActions"/>.
    /// Sub-VMs und Zustands-Callbacks bleiben separate Konstruktorparameter.
    /// </summary>
    internal sealed record TagManagerActionsContext(
        ITagService TagService,
        ITagLookupCoordinator LookupCoordinator,
        IFileRenameService FileRenameService,
        IErrorDialogService ErrorDialogService,
        IConfirmationDialogService ConfirmationDialogService,
        IOnlineAccessGuard OnlineAccessGuard);
}
