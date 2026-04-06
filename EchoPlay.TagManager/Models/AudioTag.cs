namespace EchoPlay.TagManager.Models
{
    /// <summary>
    /// Flaches Datenmodell für alle Standard-Audio-Metadaten.
    /// Wird von <see cref="Abstractions.ITagService"/> als Lese- und Schreib-DTO verwendet.
    /// Felder mit dem Wert <see langword="null"/> werden beim Schreiben ignoriert –
    /// der bestehende Wert in der Datei bleibt erhalten.
    /// </summary>
    public sealed class AudioTag
    {
        /// <summary>Titel des Tracks.</summary>
        public string? Title { get; set; }

        /// <summary>Album, zu dem der Track gehört.</summary>
        public string? Album { get; set; }

        /// <summary>Haupt-Interpret des Tracks.</summary>
        public string? Artist { get; set; }

        /// <summary>
        /// Albumkünstler – oft abweichend vom Track-Künstler,
        /// z.B. "TKKG" als Albumkünstler statt der einzelnen Sprecher.
        /// </summary>
        public string? AlbumArtist { get; set; }

        /// <summary>Kommentar-Feld des Tags.</summary>
        public string? Comment { get; set; }

        /// <summary>Genre des Tracks, z.B. "Hörbuch" oder "Hörspiel".</summary>
        public string? Genre { get; set; }

        /// <summary>Erscheinungsjahr des Albums.</summary>
        public uint? Year { get; set; }

        /// <summary>Nummer dieses Tracks innerhalb des Albums (1-basiert).</summary>
        public uint? TrackNumber { get; set; }

        /// <summary>Gesamtanzahl der Tracks auf dem Album.</summary>
        public uint? TrackCount { get; set; }

        /// <summary>Disk-Nummer bei Multi-Disk-Alben (1-basiert).</summary>
        public uint? DiscNumber { get; set; }

        /// <summary>Gesamtanzahl der Disks bei Multi-Disk-Alben.</summary>
        public uint? DiscCount { get; set; }

        /// <summary>
        /// Rohdaten des Cover-Bilds als Byte-Array.
        /// <see langword="null"/> bedeutet, dass kein Cover vorhanden ist oder beim
        /// Schreiben das vorhandene Cover nicht überschrieben werden soll.
        /// </summary>
        public byte[]? CoverImageData { get; set; }

        /// <summary>
        /// MIME-Typ des Cover-Bilds, z.B. <c>"image/jpeg"</c> oder <c>"image/png"</c>.
        /// Wird nur ausgewertet, wenn <see cref="CoverImageData"/> gesetzt ist.
        /// </summary>
        public string? CoverMimeType { get; set; }
    }
}
