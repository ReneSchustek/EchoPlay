namespace EchoPlay.App.Services
{
    /// <summary>
    /// Berichtigt die Angaben einer lokalen Folge oder Serie. Eine Folge wird in der Datenbank,
    /// in den Tags ihrer Audiodateien und im Ordnernamen geändert, damit die Änderung das
    /// nächste Einlesen übersteht: Das Einlesen nimmt Nummer und Titel aus dem Ordnernamen.
    /// </summary>
    public interface ILocalMetadataEditor
    {
        /// <summary>
        /// Liest die bearbeitbaren Angaben einer Folge.
        /// </summary>
        /// <param name="episodeId">Die Folge.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Angaben, oder <see langword="null"/>, wenn es die Folge nicht gibt.</returns>
        Task<EpisodeEditData?> LoadEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Speichert die Angaben einer Folge in Datenbank, Tags und Ordnernamen.
        /// </summary>
        /// <param name="edit">Die neuen Angaben; geprüft durch <see cref="ViewModels.LocalMetadataValidation"/>.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Was geschrieben und ob der Ordner umbenannt wurde.</returns>
        Task<EpisodeEditOutcome> SaveEpisodeAsync(EpisodeEditData edit, CancellationToken cancellationToken = default);

        /// <summary>
        /// Speichert den Titel einer Serie in der Datenbank. Das Einlesen setzt den Serientitel
        /// nur beim Anlegen; eine Änderung bleibt deshalb stehen.
        /// </summary>
        /// <param name="seriesId">Die Serie.</param>
        /// <param name="title">Der neue Titel, nicht leer.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><see langword="true"/>, wenn die Serie gefunden und gespeichert wurde.</returns>
        Task<bool> SaveSeriesTitleAsync(Guid seriesId, string title, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Die bearbeitbaren Angaben einer Folge.
    /// </summary>
    /// <param name="EpisodeId">Die Folge.</param>
    /// <param name="Title">Titel der Folge, landet auch im Album-Tag.</param>
    /// <param name="Number">Folgennummer; <see langword="null"/> bei einer Sonderfolge.</param>
    /// <param name="Artist">Interpret aus dem Tag der ersten Datei; leer lässt den Tag unverändert.</param>
    /// <param name="Year">Erscheinungsjahr; <see langword="null"/>, wenn unbekannt.</param>
    public sealed record EpisodeEditData(Guid EpisodeId, string Title, int? Number, string? Artist, int? Year);

    /// <summary>
    /// Ergebnis des Speicherns einer Folge.
    /// </summary>
    /// <param name="TagsWritten">Dateien mit neuen Tags.</param>
    /// <param name="TagsFailed">Dateien, deren Tags sich nicht schreiben ließen.</param>
    /// <param name="FolderRenamed">Ob der Ordner nach dem eingestellten Muster umbenannt wurde.</param>
    public sealed record EpisodeEditOutcome(int TagsWritten, int TagsFailed, bool FolderRenamed);
}
