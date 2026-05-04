using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Logger.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILogViewerCoordinator"/>. Verwaltet eine in-memory Log-Entry-Liste
    /// und gibt vorkonfigurierte Dateioptionen/Zeilen zurück. Tests können Einträge direkt
    /// hinzufügen und prüfen, wie der Aufrufer filtert und formatiert.
    /// </summary>
    internal sealed class FakeLogViewerCoordinator : ILogViewerCoordinator
    {
        private readonly List<LogEntry> _liveEntries = [];

        /// <summary>
        /// Wenn <see langword="true"/>, verhält sich der Fake so, als sei ein MemorySink registriert,
        /// und <see cref="BuildFilteredLiveEntries"/> liefert die in <see cref="_liveEntries"/> gesammelten Einträge.
        /// </summary>
        public bool IsLiveViewAvailable { get; set; } = true;

        /// <summary>Die Rückgabe für <see cref="LoadLogFileOptionsAsync"/>.</summary>
        public List<LogFileOption> FileOptions { get; } = [];

        /// <summary>Mapping Dateipfad → Dateizeilen für <see cref="LoadFileLinesAsync"/>.</summary>
        public Dictionary<string, IReadOnlyList<string>> FileContents { get; } = [];

        /// <summary>Fügt einen Live-Log-Eintrag für spätere Filter-Abfragen hinzu.</summary>
        /// <param name="entry">Der einzufügende Eintrag.</param>
        public void AddLiveEntry(LogEntry entry) => _liveEntries.Add(entry);

        /// <inheritdoc/>
        public Task<IReadOnlyList<LogFileOption>> LoadLogFileOptionsAsync()
        {
            // Live-Option zuerst – identisches Verhalten wie in der echten Implementierung
            List<LogFileOption> result = [new LogFileOption("Aktuell (Live)", DateTime.MaxValue, null), .. FileOptions];
            return Task.FromResult<IReadOnlyList<LogFileOption>>(result);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<string>> LoadFileLinesAsync(string filePath)
        {
            if (FileContents.TryGetValue(filePath, out IReadOnlyList<string>? lines))
            {
                return Task.FromResult(lines);
            }

            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> BuildFilteredLiveEntries(string searchText, LogLevel minimumLevel)
        {
            if (!IsLiveViewAvailable)
            {
                return [];
            }

            List<string> result = [];
            foreach (LogEntry entry in _liveEntries)
            {
                if (entry.Level < minimumLevel)
                {
                    continue;
                }

                string line = Format(entry);
                if (!string.IsNullOrWhiteSpace(searchText)
                    && !line.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(line);
            }

            return result;
        }

        private static string Format(LogEntry entry)
        {
            // Spiegelt die Konvertierung des produktiven LogViewerCoordinator (UTC -> Lokalzeit).
            string time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string line = $"{time} [{entry.Level}] {entry.Category}: {entry.Message}";
            if (entry.Exception is not null)
            {
                line += $" ({entry.Exception.Message})";
            }
            return line;
        }
    }
}
