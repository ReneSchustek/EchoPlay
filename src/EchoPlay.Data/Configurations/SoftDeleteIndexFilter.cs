namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// Die Filter der Teilindizes, die nur aktive oder nur logisch gelöschte Zeilen enthalten.
    /// </summary>
    /// <remarks>
    /// SQLite darf einen Teilindex nur benutzen, wenn es aus der Abfrage ableiten kann, dass der
    /// Filter des Index erfüllt ist. Diese Ableitung ist rein formal: Aus <c>NOT "IsDeleted"</c> folgt
    /// für SQLite nicht <c>IsDeleted = 0</c>, obwohl beides dieselben Zeilen meint. Der Löschfilter
    /// von EF Core schreibt <c>NOT ("x"."IsDeleted")</c> in jede Abfrage, die Bereinigung
    /// <c>"x"."IsDeleted"</c>. Die Filter stehen deshalb in genau dieser Form; mit
    /// <c>IsDeleted = 0</c> war jeder dieser Indizes für EF-Abfragen unbrauchbar, und SQLite ging
    /// etwa beim Stapelzugriff auf die Cover alle Cover einer Art durch.
    /// <para>
    /// Ein Test prüft, dass kein Index mit Löschfilter eine andere Form benutzt.
    /// </para>
    /// </remarks>
    public static class SoftDeleteIndexFilter
    {
        /// <summary>Filter für Indizes über aktive Zeilen, passend zum Löschfilter von EF Core.</summary>
        public const string ActiveRows = "NOT \"IsDeleted\"";

        /// <summary>Filter für Indizes über logisch gelöschte Zeilen, passend zur Bereinigung.</summary>
        public const string DeletedRows = "\"IsDeleted\"";
    }
}
