using EchoPlay.App.Services;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Dienste, die jede Folgen-Kachel der Startseite für ihre Befehle braucht:
    /// Wiedergabe starten, Hörstatus ändern, Hinweise und Rückfragen anzeigen, Texte
    /// übersetzen. Sie werden gebündelt gereicht, weil sie immer gemeinsam auftreten und
    /// jede Kachel-Erzeugung sie unverändert durchreicht.
    /// </summary>
    /// <param name="ErrorDialogService">Zeigt Hinweise, wenn eine Folge nicht verfügbar ist.</param>
    /// <param name="ConfirmationDialogService">Fragt vor Änderungen am Hörstatus nach.</param>
    /// <param name="PlayerService">Startet die Wiedergabe.</param>
    /// <param name="LocalizationService">Liefert die Anzeigetexte. Ohne ihn gelten die Vorgabewerte.</param>
    internal sealed record NewEpisodeCardServices(
        IErrorDialogService ErrorDialogService,
        IConfirmationDialogService ConfirmationDialogService,
        IPlayerService PlayerService,
        ILocalizationService? LocalizationService);
}
