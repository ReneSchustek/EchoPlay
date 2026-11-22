namespace EchoPlay.Data.Entities.Contracts
{
    /// <summary>
    /// Wohin ein Feld gehört, wenn Daten den Rechner verlassen: Export, Umzug und Abgleich lesen
    /// diese Einteilung, statt eine eigene Liste zu führen.
    /// </summary>
    public enum DataScope
    {
        /// <summary>Fachliche Daten des Nutzers; reisen mit und werden zwischen Geräten abgeglichen.</summary>
        Shared,

        /// <summary>
        /// Gehört zu diesem Rechner, etwa Ordner, Lautstärke oder Protokolleinstellungen. Wird nie
        /// abgeglichen; Pfade darunter rechnet <see cref="Core.Replication.LibraryPathMapper"/> an den
        /// Grenzen um.
        /// </summary>
        DeviceBound,

        /// <summary>Lässt sich jederzeit neu laden, etwa Cover; wird weder abgeglichen noch zwingend exportiert.</summary>
        Cache,

        /// <summary>
        /// Kennung oder Fremdschlüssel der Datenbank. Sie entsteht je Gerät; im Abgleich ersetzt sie
        /// der fachliche Schlüssel aus <see cref="Core.Replication.BusinessKey"/>.
        /// </summary>
        LocalIdentity,
    }

    /// <summary>
    /// Ordnet ein gespeichertes Feld einer <see cref="DataScope"/> zu.
    /// </summary>
    /// <remarks>
    /// Die Einteilung steht am Feld, nicht in einer Liste daneben, damit sie mit dem Feld entsteht
    /// und mit ihm verschwindet. Ein Test lässt jede gespeicherte Eigenschaft ohne Einteilung
    /// scheitern; eine neue Spalte kann deshalb nicht unbemerkt in Export oder Abgleich geraten.
    /// </remarks>
    /// <param name="scope">Die Einteilung.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class DataScopeAttribute(DataScope scope) : Attribute
    {
        /// <summary>Die Einteilung.</summary>
        public DataScope Scope { get; } = scope;
    }
}
