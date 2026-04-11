using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.TagManager.Models;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Umbenennungs-Sektion des Tag-Managers.
    /// Hält das Muster und die berechnete Vorschau. Die eigentliche Berechnung und
    /// Ausführung der Umbenennung koordiniert das übergeordnete <see cref="TagManagerViewModel"/>,
    /// weil sie die Dateiliste und den Tag-Service braucht.
    /// </summary>
    public sealed class TagRenameViewModel : ObservableObject
    {
        // Intern gehaltene Vorschau-Items vom Modul-Typ – für die tatsächliche Umbenennung
        private IReadOnlyList<RenamePreviewItem> _renamePreviewItems = [];

        // App-eigene Display-Modelle, an die die Page bindet
        private IReadOnlyList<RenamePreviewDisplay> _renamePreview = [];
        private string _renamePattern = "{track:00} - {title}";

        /// <summary>
        /// Muster für die Umbenennung, z.B. <c>"{track:00} - {title}"</c>.
        /// Unterstützte Platzhalter: {title}, {album}, {artist}, {year},
        /// {track}, {track:00}, {track:000}, {filename}.
        /// </summary>
        public string RenamePattern
        {
            get => _renamePattern;
            set
            {
                if (SetProperty(ref _renamePattern, value))
                {
                    // Nach Musteränderung alte Vorschau zurücksetzen – sie wäre veraltet
                    SetPreviewItems([]);
                }
            }
        }

        /// <summary>
        /// Vorschau der Umbenennung als Display-Modelle für die Page-Bindung.
        /// </summary>
        public IReadOnlyList<RenamePreviewDisplay> RenamePreview
        {
            get => _renamePreview;
            private set
            {
                if (SetProperty(ref _renamePreview, value))
                {
                    OnPropertyChanged(nameof(RenamePreviewVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit der Vorschauliste – sichtbar sobald eine Vorschau berechnet wurde.
        /// </summary>
        public Visibility RenamePreviewVisibility =>
            _renamePreviewItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Liefert die roh gehaltene Preview-Item-Liste für die Ausführung durch den Rename-Service.
        /// </summary>
        public IReadOnlyList<RenamePreviewItem> PreviewItems => _renamePreviewItems;

        /// <summary>
        /// Ersetzt die Vorschau-Items und mappt sie für die UI-Bindung auf die Display-Modelle.
        /// </summary>
        /// <param name="items">Die vom <see cref="EchoPlay.TagManager.Abstractions.IFileRenameService"/> berechneten Items.</param>
        public void SetPreviewItems(IReadOnlyList<RenamePreviewItem> items)
        {
            _renamePreviewItems = items;

            List<RenamePreviewDisplay> displays = new(items.Count);
            foreach (RenamePreviewItem item in items)
            {
                displays.Add(new RenamePreviewDisplay(item.OldName, item.NewName));
            }
            RenamePreview = displays;
        }
    }
}
