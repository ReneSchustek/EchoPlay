using EchoPlay.App.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Gemeinsame Basis für Kachel-ViewModels mit einem nachladbaren Cover-Bild.
    /// Kapselt das Cover samt Platzhalter-Sichtbarkeit, das sonst in mehreren Kachel-VMs
    /// wortgleich wiederholt wäre.
    /// </summary>
    public abstract class CoverCardViewModelBase : ObservableObject
    {
        private BitmapImage? _coverImage;

        /// <summary>
        /// Cover-Bild der Kachel; wird häufig nachträglich gesetzt (Download/Cache).
        /// Änderungen benachrichtigen zusätzlich <see cref="NoCoverVisibility"/>.
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            set
            {
                if (SetProperty(ref _coverImage, value))
                {
                    // NoCoverVisibility hängt direkt vom Cover ab – bei Änderung mitfeuern
                    OnPropertyChanged(nameof(NoCoverVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Cover-Platzhalters, wenn kein Bild vorhanden ist.</summary>
        public Visibility NoCoverVisibility =>
            _coverImage is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Setzt das Cover-Bild zurück – gibt hunderte Kachel-Bitmaps frühzeitig frei,
        /// statt sie bis zum nächsten GC-Lauf am Heap zu halten.
        /// </summary>
        public void ClearCoverImage()
        {
            CoverImage = null;
        }
    }
}
