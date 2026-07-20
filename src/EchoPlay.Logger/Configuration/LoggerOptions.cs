namespace EchoPlay.Logger.Configuration
{
    /// <summary>
    /// Konfigurationsoptionen für den Logger.
    /// </summary>
    public sealed class LoggerOptions
    {
        /// <summary>
        /// Verzeichnis für Log-Dateien.
        /// </summary>
        public string LogDirectory { get; set; } = "logs";

        /// <summary>
        /// Maximale Dateigröße in MB bevor rotiert wird.
        /// </summary>
        public int MaxFileSizeMb { get; set; } = 10;

        /// <summary>
        /// Minimales Log-Level das geschrieben wird.
        /// </summary>
        public Models.LogLevel MinimumLevel { get; set; } = Models.LogLevel.Information;

        /// <summary>
        /// Aktiviert Ausgabe in die Debug-Konsole.
        /// </summary>
        public bool EnableDebugConsole { get; set; } = true;

        /// <summary>
        /// Aktiviert Ausgabe in Dateien.
        /// </summary>
        public bool EnableFileLogging { get; set; } = true;

        /// <summary>
        /// Anzahl Tage nach denen alte Logs gelöscht werden.
        /// 0 = Deaktiviert.
        /// </summary>
        public int RetentionDays { get; set; } = 30;

        /// <summary>
        /// Maximale Gesamtgröße aller Logs in MB.
        /// Älteste Logs werden gelöscht wenn überschritten.
        /// 0 = Deaktiviert.
        /// </summary>
        public int MaxTotalSizeMb { get; set; } = 100;

        /// <summary>
        /// Aktiviert automatische Bereinigung alter Logs.
        /// </summary>
        public bool EnableAutoCleanup { get; set; } = true;

        /// <summary>
        /// Aktiviert den In-Memory-Puffer für den Log-Viewer.
        /// </summary>
        public bool EnableMemorySink { get; set; }

        /// <summary>
        /// Maximale Anzahl der im Arbeitsspeicher gepufferten Einträge.
        /// Nur relevant wenn <see cref="EnableMemorySink"/> aktiv ist.
        /// </summary>
        public int MemorySinkCapacity { get; set; } = 100;

        /// <summary>
        /// Aktiviert die JSON-Lines-Ausgabe (ein JSON-Objekt pro Zeile) in eine separate Datei.
        /// Für strukturiertes Log-Shipping an externe Tools (z. B. Observability-Stacks).
        /// </summary>
        public bool EnableJsonSink { get; set; }

        /// <summary>
        /// Verzeichnis für JSON-Log-Dateien. Wird nur verwendet wenn <see cref="EnableJsonSink"/> aktiv ist.
        /// Default ist das normale <see cref="LogDirectory"/>; kann für getrennte Rotation gesetzt werden.
        /// </summary>
        public string? JsonLogDirectory { get; set; }
    }
}
