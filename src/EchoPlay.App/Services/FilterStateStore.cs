using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Hält die Such- und Filterkriterien der Seiten über einen Seitenwechsel hinweg.
    /// <para>
    /// Nötig, weil die ViewModels der Seiten kurzlebig sind: Beim Verlassen werden sie
    /// freigegeben, beim Zurückkehren neu erzeugt. Ohne diese Ablage stünde die Liste nach
    /// jedem Ausflug in eine Detailansicht wieder ungefiltert da — und der Nutzer tippt seine
    /// Suche jedes Mal neu.
    /// </para>
    /// <para>
    /// Bewusst nur im Arbeitsspeicher: Der Zustand soll den Weg durch die Anwendung
    /// überstehen, nicht das Beenden. Wer die Anwendung neu startet, erwartet den vollen
    /// Bestand.
    /// </para>
    /// </summary>
    public sealed class FilterStateStore
    {
        private readonly Dictionary<string, object> _states = [];

        /// <summary>
        /// Liefert die Kriterien einer Seite und legt sie beim ersten Zugriff an.
        /// </summary>
        /// <typeparam name="T">Typ der Kriterien.</typeparam>
        /// <param name="pageKey">Schlüssel der Seite, etwa <c>MediathekLokal</c>.</param>
        /// <returns>Dasselbe Objekt bei jedem weiteren Aufruf mit demselben Schlüssel.</returns>
        /// <exception cref="ArgumentException"><paramref name="pageKey"/> ist leer.</exception>
        public T GetOrCreate<T>(string pageKey) where T : class, new()
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pageKey);

            // Der Typ gehört in den Schlüssel: Zwei Seiten könnten denselben Namen tragen und
            // verschiedene Kriterien führen — dann bekäme eine von beiden ein Objekt des
            // falschen Typs und der Zugriff bräche zur Laufzeit.
            string key = $"{pageKey}:{typeof(T).FullName}";

            if (!_states.TryGetValue(key, out object? existing))
            {
                existing = new T();
                _states[key] = existing;
            }

            return (T)existing;
        }

        /// <summary>
        /// Verwirft alle gemerkten Kriterien. Für den Fall, dass der Bestand komplett neu
        /// aufgebaut wird und ein alter Filter nur noch ins Leere zeigte.
        /// </summary>
        public void Clear() => _states.Clear();
    }
}
