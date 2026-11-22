using System.Windows.Input;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Eine Zeile in der Geräteliste des Abgleichs: Name, letzter Kontakt, übernommene Sätze, ein
    /// Fehler, falls das Lesen stehen blieb, und „Entfernen“.
    /// </summary>
    /// <param name="deviceId">Kennung des Geräts.</param>
    /// <param name="name">Kurzname zur Anzeige.</param>
    /// <param name="lastSeenText">Wann zuletzt etwas von ihm übernommen wurde.</param>
    /// <param name="appliedText">Wie viel bisher übernommen wurde.</param>
    /// <param name="errorText">Warum das Lesen stehen blieb, oder leer.</param>
    /// <param name="removeCommand">Entfernt das Gerät nach Rückfrage.</param>
    public sealed class ReplicationDeviceItemViewModel(
        Guid deviceId, string name, string lastSeenText, string appliedText, string? errorText, ICommand removeCommand)
    {
        /// <summary>Kennung des Geräts.</summary>
        public Guid DeviceId { get; } = deviceId;

        /// <summary>Kurzname zur Anzeige, auch Name für Bildschirmleser.</summary>
        public string Name { get; } = name;

        /// <summary>Wann zuletzt etwas von ihm übernommen wurde.</summary>
        public string LastSeenText { get; } = lastSeenText;

        /// <summary>Wie viel bisher übernommen wurde.</summary>
        public string AppliedText { get; } = appliedText;

        /// <summary>Warum das Lesen stehen blieb, oder leer.</summary>
        public string ErrorText { get; } = errorText ?? string.Empty;

        /// <summary>Sichtbarkeit des Fehlers.</summary>
        public Visibility ErrorVisibility => string.IsNullOrEmpty(ErrorText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Entfernt das Gerät nach Rückfrage.</summary>
        public ICommand RemoveCommand { get; } = removeCommand;
    }
}
