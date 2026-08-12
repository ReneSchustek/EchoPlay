using System;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Meldet, mit welchem Text die Suche abgeschickt wurde.
    /// </summary>
    public sealed class SearchSubmittedEventArgs : EventArgs
    {
        /// <summary>
        /// Erzeugt die Ereignisdaten.
        /// </summary>
        /// <param name="query">Abgeschickter Suchtext; nie leer.</param>
        public SearchSubmittedEventArgs(string query) => Query = query;

        /// <summary>Abgeschickter Suchtext.</summary>
        public string Query { get; }
    }
}
