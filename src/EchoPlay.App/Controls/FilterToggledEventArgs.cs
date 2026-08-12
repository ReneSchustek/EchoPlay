using System;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Meldet, welcher Filter der Filterleiste umgeschaltet wurde.
    /// </summary>
    public sealed class FilterToggledEventArgs : EventArgs
    {
        /// <summary>
        /// Erzeugt die Ereignisdaten.
        /// </summary>
        /// <param name="chip">Der umgeschaltete Filter samt seinem neuen Zustand.</param>
        /// <exception cref="ArgumentNullException"><paramref name="chip"/> ist <see langword="null"/>.</exception>
        public FilterToggledEventArgs(FilterChip chip)
        {
            ArgumentNullException.ThrowIfNull(chip);
            Chip = chip;
        }

        /// <summary>Der umgeschaltete Filter.</summary>
        public FilterChip Chip { get; }
    }
}
