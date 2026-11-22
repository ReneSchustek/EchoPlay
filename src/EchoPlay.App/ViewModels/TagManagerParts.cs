namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die vier Teil-ViewModels des Tag-Managers. Die Aktionen bekommen sie gebündelt, weil sie
    /// immer gemeinsam auftreten und jede Aktion sie unverändert vom Tag-Manager übernimmt.
    /// </summary>
    /// <param name="FileList">Die Dateiliste mit Auswahl und Ordner.</param>
    /// <param name="Editor">Die Eingabefelder.</param>
    /// <param name="Cover">Das angezeigte Cover.</param>
    /// <param name="Rename">Die Vorschau des Umbenennens.</param>
    internal sealed record TagManagerParts(
        TagFileListViewModel FileList,
        TagEditorFieldsViewModel Editor,
        TagCoverViewModel Cover,
        TagRenameViewModel Rename);
}
