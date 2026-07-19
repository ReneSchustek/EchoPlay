using EchoPlay.App.Infrastructure;
using EchoPlay.TagManager.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Cover-Sektion des Tag-Managers.
    /// Hält das angezeigte Cover-Bild samt Roh-Byte-Array und MIME-Typ. Die eigentlichen
    /// Schreib-Operationen (auf Datei, auf alle Dateien, Entfernen) koordiniert das
    /// übergeordnete <see cref="TagManagerViewModel"/>, weil sie den Tag-Service brauchen.
    /// </summary>
    public sealed class TagCoverViewModel : ObservableObject
    {
        private readonly Action _onUserEdit;

        private BitmapImage? _coverImage;
        private byte[]? _coverImageData;
        private string? _coverMimeType;

        /// <summary>
        /// Initialisiert das Sub-VM mit dem Edit-Callback.
        /// </summary>
        /// <param name="onUserEdit">Wird aufgerufen, sobald der Nutzer ein neues Cover lädt.</param>
        public TagCoverViewModel(Action onUserEdit)
        {
            _onUserEdit = onUserEdit;
        }

        /// <summary>
        /// Cover-Bild zur Anzeige. Wird entweder aus den Tags einer Datei befüllt (via
        /// <see cref="LoadFromTag"/>) oder aus einem vom Nutzer ausgewählten Bild (via
        /// <see cref="SetFromFile"/>).
        /// </summary>
        public BitmapImage? CoverImage
        {
            get => _coverImage;
            private set
            {
                if (SetProperty(ref _coverImage, value))
                {
                    OnPropertyChanged(nameof(CoverVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Cover-Bilds – sichtbar wenn ein Cover vorhanden ist.
        /// </summary>
        public Visibility CoverVisibility =>
            _coverImage is not null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Rohdaten des aktuell gehaltenen Covers – benötigt für das Schreiben auf weitere
        /// Dateien (<c>ApplyCoverToAll</c>). <see langword="null"/> wenn kein Cover gesetzt ist.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Bildrohdaten werden direkt an TagLib-APIs (ByteVector) und an 'ApplyCoverToAll' uebergeben; ein ReadOnlyMemory-Wrapper wuerde nur Kopien erzeugen.")]
        public byte[]? CoverImageData => _coverImageData;

        /// <summary>
        /// MIME-Typ des aktuell gehaltenen Covers (z.B. <c>"image/jpeg"</c>).
        /// <see langword="null"/> wenn kein Cover gesetzt ist.
        /// </summary>
        public string? CoverMimeType => _coverMimeType;

        /// <summary>Gibt an, ob aktuell ein Cover gesetzt ist.</summary>
        public bool HasCover => _coverImageData is not null;

        /// <summary>
        /// Befüllt das Cover aus einem <see cref="AudioTag"/>. Wird beim Laden einer Datei
        /// aufgerufen; markiert keine Änderung, weil der Nutzer nichts gemacht hat.
        /// </summary>
        /// <param name="tag">Der gelesene Tag. <see langword="null"/> oder leere Daten leeren das Cover.</param>
        public void LoadFromTag(AudioTag? tag)
        {
            if (tag?.CoverImageData is not null && tag.CoverImageData.Length > 0)
            {
                _coverImageData = tag.CoverImageData;
                _coverMimeType = tag.CoverMimeType;

                BitmapImage bitmap = new();
                using MemoryStream stream = new(tag.CoverImageData);
                bitmap.SetSource(stream.AsRandomAccessStream());
                CoverImage = bitmap;
            }
            else
            {
                Clear();
            }
        }

        /// <summary>
        /// Setzt das Cover aus einem vom Nutzer geladenen Bild und markiert eine Änderung.
        /// </summary>
        /// <param name="imageData">Rohdaten des Bilds.</param>
        /// <param name="mimeType">MIME-Typ des Bilds (z.B. <c>"image/jpeg"</c>).</param>
        public void SetFromFile(byte[] imageData, string mimeType)
        {
            _coverImageData = imageData;
            _coverMimeType = mimeType;

            BitmapImage bitmap = new();
            using MemoryStream stream = new(imageData);
            bitmap.SetSource(stream.AsRandomAccessStream());
            CoverImage = bitmap;

            _onUserEdit();
        }

        /// <summary>
        /// Leert das Cover komplett. Wird nach erfolgreichem Entfernen aus der Datei
        /// und beim Laden einer cover-losen Datei aufgerufen.
        /// </summary>
        public void Clear()
        {
            _coverImageData = null;
            _coverMimeType = null;
            CoverImage = null;
        }
    }
}
