using EchoPlay.App.Infrastructure;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Tag-Editor-Felder (Titel, Album, Interpret …) des Tag-Managers.
    /// Hält die aktuell editierten Werte, merkt sich welche Felder geändert wurden und baut
    /// daraus wieder <see cref="AudioTag"/>-Instanzen für das Speichern. Die tatsächlichen
    /// Schreib- und Lookup-Operationen koordiniert das übergeordnete
    /// <see cref="TagManagerViewModel"/>.
    /// </summary>
    public sealed class TagEditorFieldsViewModel : ObservableObject
    {
        // Platzhalter für Felder mit unterschiedlichen Werten bei Mehrfachauswahl
        private const string MixedValuePlaceholder = "(verschieden)";

        private readonly Action _onUserEdit;

        // Merkt sich, welche Felder der Nutzer bei Mehrfachauswahl geändert hat.
        // Nur geänderte Felder werden beim Speichern auf alle selektierten Dateien geschrieben.
        private readonly HashSet<string> _editedFields = [];

        // Zwischenspeicher: gemeinsame Tags aus dem letzten Lookup-Ergebnis für "Alle taggen"
        private AudioTag? _pendingBatchTag;

        private string? _title;
        private string? _album;
        private string? _artist;
        private string? _albumArtist;
        private string? _year;
        private string? _trackNumber;
        private string? _trackCount;
        private string? _genre;

        /// <summary>
        /// Initialisiert das Sub-VM mit dem Edit-Callback.
        /// </summary>
        /// <param name="onUserEdit">Wird bei jeder Nutzeränderung an einem Feld aufgerufen.</param>
        public TagEditorFieldsViewModel(Action onUserEdit)
        {
            _onUserEdit = onUserEdit;
        }

        /// <summary>Titel des Tracks.</summary>
        public string? Title
        {
            get => _title;
            set { _ = SetProperty(ref _title, value); _ = _editedFields.Add(nameof(Title)); _onUserEdit(); }
        }

        /// <summary>Album des Tracks.</summary>
        public string? Album
        {
            get => _album;
            set { _ = SetProperty(ref _album, value); _ = _editedFields.Add(nameof(Album)); _onUserEdit(); }
        }

        /// <summary>Haupt-Interpret des Tracks.</summary>
        public string? Artist
        {
            get => _artist;
            set { _ = SetProperty(ref _artist, value); _ = _editedFields.Add(nameof(Artist)); _onUserEdit(); }
        }

        /// <summary>Album-Künstler (kann von Artist abweichen, z.B. bei Samplern).</summary>
        public string? AlbumArtist
        {
            get => _albumArtist;
            set { _ = SetProperty(ref _albumArtist, value); _ = _editedFields.Add(nameof(AlbumArtist)); _onUserEdit(); }
        }

        /// <summary>Erscheinungsjahr als Text – muss beim Speichern in <c>uint</c> geparst werden.</summary>
        public string? Year
        {
            get => _year;
            set { _ = SetProperty(ref _year, value); _ = _editedFields.Add(nameof(Year)); _onUserEdit(); }
        }

        /// <summary>Tracknummer als Text.</summary>
        public string? TrackNumber
        {
            get => _trackNumber;
            set { _ = SetProperty(ref _trackNumber, value); _ = _editedFields.Add(nameof(TrackNumber)); _onUserEdit(); }
        }

        /// <summary>Gesamtanzahl Tracks als Text.</summary>
        public string? TrackCount
        {
            get => _trackCount;
            set { _ = SetProperty(ref _trackCount, value); _ = _editedFields.Add(nameof(TrackCount)); _onUserEdit(); }
        }

        /// <summary>Genre des Tracks.</summary>
        public string? Genre
        {
            get => _genre;
            set { _ = SetProperty(ref _genre, value); _ = _editedFields.Add(nameof(Genre)); _onUserEdit(); }
        }

        /// <summary>
        /// Ob ein Batch-Tag aus einem Lookup bereitsteht und auf alle Dateien angewendet werden kann.
        /// </summary>
        public bool HasPendingBatchTag => _pendingBatchTag is not null;

        /// <summary>Gibt den zwischengespeicherten Batch-Tag für <c>ApplyToAll</c> zurück.</summary>
        public AudioTag? PendingBatchTag => _pendingBatchTag;

        /// <summary>Löscht den zwischengespeicherten Batch-Tag nach erfolgreichem Apply.</summary>
        public void ClearPendingBatchTag()
        {
            _pendingBatchTag = null;
            OnPropertyChanged(nameof(HasPendingBatchTag));
        }

        /// <summary>
        /// Befüllt die Formularfelder mit den Werten eines <see cref="AudioTag"/>.
        /// Markiert bewusst keine Änderung – der Zustand wird nur aus der Datei geladen.
        /// </summary>
        /// <param name="tag">Die gelesenen Tags einer Datei.</param>
        public void PopulateFromTag(AudioTag tag)
        {
            ArgumentNullException.ThrowIfNull(tag);
            // Setter umgehen, damit keine Nutzeränderung gemeldet wird.
            _title       = tag.Title;
            _album       = tag.Album;
            _artist      = tag.Artist;
            _albumArtist = tag.AlbumArtist;
            _year        = tag.Year?.ToString(CultureInfo.InvariantCulture);
            _trackNumber = tag.TrackNumber?.ToString(CultureInfo.InvariantCulture);
            _trackCount  = tag.TrackCount?.ToString(CultureInfo.InvariantCulture);
            _genre       = tag.Genre;

            RaiseAllFieldChanges();
        }

        /// <summary>
        /// Befüllt die Felder aus mehreren Dateien. Gemeinsame Werte werden übernommen;
        /// Felder mit unterschiedlichen Werten zeigen den Platzhalter „(verschieden)".
        /// </summary>
        /// <param name="tags">Die gelesenen Tags der selektierten Dateien. Mindestens ein Eintrag.</param>
        public void PopulateFromMultipleTags(IReadOnlyList<AudioTag> tags)
        {
            ArgumentNullException.ThrowIfNull(tags);
            _editedFields.Clear();

            AudioTag first = tags[0];

            _title       = tags.All(t => t.Title == first.Title)             ? first.Title       : MixedValuePlaceholder;
            _album       = tags.All(t => t.Album == first.Album)             ? first.Album       : MixedValuePlaceholder;
            _artist      = tags.All(t => t.Artist == first.Artist)           ? first.Artist      : MixedValuePlaceholder;
            _albumArtist = tags.All(t => t.AlbumArtist == first.AlbumArtist) ? first.AlbumArtist : MixedValuePlaceholder;
            _genre       = tags.All(t => t.Genre == first.Genre)             ? first.Genre       : MixedValuePlaceholder;

            _year = tags.All(t => t.Year == first.Year)
                ? first.Year?.ToString(CultureInfo.InvariantCulture)
                : MixedValuePlaceholder;

            _trackNumber = tags.All(t => t.TrackNumber == first.TrackNumber)
                ? first.TrackNumber?.ToString(CultureInfo.InvariantCulture)
                : MixedValuePlaceholder;

            _trackCount = tags.All(t => t.TrackCount == first.TrackCount)
                ? first.TrackCount?.ToString(CultureInfo.InvariantCulture)
                : MixedValuePlaceholder;

            RaiseAllFieldChanges();
        }

        /// <summary>
        /// Leert alle Formularfelder und den „geänderte Felder"-Speicher.
        /// </summary>
        public void Clear()
        {
            _editedFields.Clear();
            _title = _album = _artist = _albumArtist = _year = _trackNumber = _trackCount = _genre = null;
            RaiseAllFieldChanges();
        }

        /// <summary>
        /// Übernimmt Felder aus einem Lookup-Ergebnis ins Formular und speichert gleichzeitig
        /// die gemeinsamen Felder als Batch-Tag für <c>ApplyToAll</c> zwischen.
        /// Nur nicht-null-Felder werden übernommen – bestehende Werte bleiben erhalten.
        /// </summary>
        /// <param name="result">Das vom Nutzer gewählte oder vom Auto-Lookup gefundene Ergebnis.</param>
        public void ApplyLookupResult(TagLookupResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.Title is not null)   Title      = result.Title;
            if (result.Artist is not null)  Artist     = result.Artist;
            if (result.Album is not null)   Album      = result.Album;
            if (result.Genre is not null)   Genre      = result.Genre;
            if (result.Year.HasValue)       Year       = result.Year.Value.ToString(CultureInfo.InvariantCulture);
            if (result.TrackCount.HasValue) TrackCount = result.TrackCount.Value.ToString(CultureInfo.InvariantCulture);

            // Gemeinsame Tags für "Alle taggen" zwischenspeichern
            _pendingBatchTag = new AudioTag
            {
                Album       = result.Album,
                Artist      = result.Artist,
                AlbumArtist = result.Artist,
                Genre       = result.Genre,
                Year        = result.Year,
                TrackCount  = result.TrackCount
            };
            OnPropertyChanged(nameof(HasPendingBatchTag));
        }

        /// <summary>
        /// Baut ein <see cref="AudioTag"/> aus den aktuellen Formularfeldern zusammen.
        /// Leere Strings werden als <see langword="null"/> übergeben, damit TagLib# bestehende Werte entfernt.
        /// Jahreszahl und Tracknummern werden aus Strings geparst – ungültige Eingaben werden ignoriert.
        /// </summary>
        public AudioTag BuildAudioTagFromFields()
        {
            return new AudioTag
            {
                Title       = NullIfEmpty(_title),
                Album       = NullIfEmpty(_album),
                Artist      = NullIfEmpty(_artist),
                AlbumArtist = NullIfEmpty(_albumArtist),
                Genre       = NullIfEmpty(_genre),
                Year        = uint.TryParse(_year, out uint year)          ? year     : null,
                TrackNumber = uint.TryParse(_trackNumber, out uint trkNum) ? trkNum   : null,
                TrackCount  = uint.TryParse(_trackCount, out uint trkCnt)  ? trkCnt   : null
            };
        }

        /// <summary>
        /// Baut ein <see cref="AudioTag"/> nur mit den bei Mehrfachauswahl tatsächlich geänderten
        /// Feldern. Nicht-geänderte Felder sind <see langword="null"/> und werden beim Merge ignoriert.
        /// </summary>
        public AudioTag BuildEditedFieldsTag()
        {
            return new AudioTag
            {
                Title       = _editedFields.Contains(nameof(Title))       ? NullIfEmpty(_title)       : null,
                Album       = _editedFields.Contains(nameof(Album))       ? NullIfEmpty(_album)       : null,
                Artist      = _editedFields.Contains(nameof(Artist))      ? NullIfEmpty(_artist)      : null,
                AlbumArtist = _editedFields.Contains(nameof(AlbumArtist)) ? NullIfEmpty(_albumArtist) : null,
                Genre       = _editedFields.Contains(nameof(Genre))       ? NullIfEmpty(_genre)       : null,
                Year        = _editedFields.Contains(nameof(Year)) && uint.TryParse(_year, out uint y)             ? y   : null,
                TrackNumber = _editedFields.Contains(nameof(TrackNumber)) && uint.TryParse(_trackNumber, out uint n) ? n : null,
                TrackCount  = _editedFields.Contains(nameof(TrackCount)) && uint.TryParse(_trackCount, out uint c)   ? c : null
            };
        }

        /// <summary>
        /// Baut ein <see cref="AudioTag"/> nur mit den gemeinsamen Feldern aus dem Formular.
        /// Title und TrackNumber werden bewusst ausgelassen – die bleiben datei-individuell.
        /// </summary>
        public AudioTag BuildSharedTagFromFields()
        {
            return new AudioTag
            {
                Album       = NullIfEmpty(_album),
                Artist      = NullIfEmpty(_artist),
                AlbumArtist = NullIfEmpty(_albumArtist),
                Genre       = NullIfEmpty(_genre),
                Year        = uint.TryParse(_year, out uint year) ? year : null,
                TrackCount  = uint.TryParse(_trackCount, out uint trkCnt) ? trkCnt : null
            };
        }

        /// <summary>
        /// Gibt die Anzahl der bisher vom Nutzer geänderten Felder zurück. Wird im Top-VM
        /// benötigt, um bei Mehrfachauswahl zu entscheiden, ob überhaupt gespeichert werden muss.
        /// </summary>
        public int EditedFieldCount => _editedFields.Count;

        /// <summary>Löscht den Merker der editierten Felder nach erfolgreichem Speichern.</summary>
        public void ResetEditedFields() => _editedFields.Clear();

        private void RaiseAllFieldChanges()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Album));
            OnPropertyChanged(nameof(Artist));
            OnPropertyChanged(nameof(AlbumArtist));
            OnPropertyChanged(nameof(Year));
            OnPropertyChanged(nameof(TrackNumber));
            OnPropertyChanged(nameof(TrackCount));
            OnPropertyChanged(nameof(Genre));
        }

        private static string? NullIfEmpty(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
