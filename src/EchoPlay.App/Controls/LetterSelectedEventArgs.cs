using System;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Meldet, welcher Buchstabe in der Sprungleiste gewählt wurde.
    /// </summary>
    public sealed class LetterSelectedEventArgs : EventArgs
    {
        /// <summary>
        /// Erzeugt die Ereignisdaten.
        /// </summary>
        /// <param name="letter">Gewählter Buchstabe, <c>A</c>–<c>Z</c> oder <c>#</c>.</param>
        public LetterSelectedEventArgs(char letter) => Letter = letter;

        /// <summary>Gewählter Buchstabe.</summary>
        public char Letter { get; }
    }
}
