using System.Diagnostics.CodeAnalysis;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Models;

namespace EchoPlay.Logger.Sinks
{
    /// <summary>
    /// Puffert die letzten N Log-Einträge im Arbeitsspeicher und meldet neue Einträge
    /// in Echtzeit über <see cref="ILiveLogSink.LogEntryAdded"/>.
    /// <para>
    /// Der Puffer arbeitet als Ringpuffer: Sobald die Kapazität erreicht ist,
    /// fällt der älteste Eintrag heraus.
    /// Der Zugriff ist thread-sicher, denn Protokollaufrufe aus Hintergrund-Threads schreiben,
    /// während der UI-Thread die Anzeige liest.
    /// Das Event wird außerhalb des Locks gefeuert, um Deadlocks auszuschließen.
    /// </para>
    /// </summary>
    public sealed class MemorySink : ILogSink, ILiveLogSink
    {
        /// <summary>
        /// Obergrenze der Puffergröße. Schützt vor versehentlicher Speicher-Eskalation;
        /// 100 000 Einträge entsprechen bei ~250 Byte pro Eintrag ca. 25 MB.
        /// </summary>
        public const int MaxCapacity = 100_000;

        private readonly Queue<LogEntry> _entries;
        private readonly int _capacity;
        private readonly object _lock = new();

        /// <summary>
        /// Erstellt einen neuen MemorySink mit der angegebenen Puffergröße.
        /// </summary>
        /// <param name="capacity">Maximale Anzahl der gepufferten Einträge. Standard: 100.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Wird ausgelöst, wenn <paramref name="capacity"/> &lt; 1 oder &gt; <see cref="MaxCapacity"/>.
        /// </exception>
        public MemorySink(int capacity = 100)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, MaxCapacity);

            _capacity = capacity;
            _entries = new(capacity);
        }

        /// <inheritdoc/>
        public event EventHandler<LogEntryEventArgs>? LogEntryAdded;

        /// <summary>
        /// Fügt einen Eintrag in den Puffer ein und feuert anschließend <see cref="LogEntryAdded"/>.
        /// Liegt die Kapazität bereits vor, wird der älteste Eintrag verworfen.
        /// Das Event wird außerhalb des Locks gefeuert, damit Subscriber nicht in einen Deadlock
        /// geraten können, falls sie ihrerseits auf den Puffer zugreifen.
        /// </summary>
        /// <param name="entry">Der zu puffernde Log-Eintrag.</param>
        /// <returns>Sofort abgeschlossener Task, denn der Puffer arbeitet synchron.</returns>
        public Task WriteAsync(LogEntry entry)
        {
            lock (_lock)
            {
                if (_entries.Count >= _capacity)
                {
                    _ = _entries.Dequeue();
                }

                _entries.Enqueue(entry);
            }

            // Außerhalb des Locks feuern; Subscriber dürfen GetEntries() aufrufen
            LogEntryAdded?.Invoke(this, new LogEntryEventArgs(entry));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gibt eine Momentaufnahme aller gepufferten Einträge zurück, älteste zuerst.
        /// </summary>
        /// <returns>
        /// Unveränderliche Liste der aktuell gepufferten Einträge.
        /// Kann leer sein, wenn noch keine Einträge geschrieben wurden.
        /// </returns>
        [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Jeder Aufruf legt eine neue, thread-sichere Kopie des Ringpuffers an. Als Eigenschaft sähe ein wiederholter Zugriff billig aus; die Regeldokumentation nennt genau diesen Fall als Grund für eine Methode.")]
        public IReadOnlyList<LogEntry> GetEntries()
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }
}
