using EchoPlay.Core.Models;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Entscheidet, ob ein gesetztes Häkchen tatsächlich gemerkt wird.
    /// </summary>
    /// <remarks>
    /// Eigene Klasse, weil die Regel keine Oberfläche braucht: Das Zeigen des Dialogs ist
    /// ohne Fensterkontext nicht prüfbar, diese Regel schon — und sie ist der Teil, bei dem
    /// ein Fehler weh tut.
    /// </remarks>
    public static class DialogSuppressionDecision
    {
        /// <summary>
        /// Gibt an, ob die Unterdrückung nach dem Schließen des Dialogs gespeichert wird.
        /// </summary>
        /// <param name="key">Der gezeigte Dialog.</param>
        /// <param name="isCheckBoxChecked">Ob der Nutzer das Häkchen gesetzt hat.</param>
        /// <param name="isAffirmative">
        /// Bei Rückfragen: ob der Nutzer zugestimmt hat. Bei Hinweisen immer
        /// <see langword="true"/> — dort gibt es nur eine Schaltfläche.
        /// </param>
        /// <returns><see langword="true"/>, wenn der Dialog künftig ausbleiben soll.</returns>
        /// <remarks>
        /// Eine gemerkte Ablehnung gibt es bewusst nicht: Sie würde die Schaltfläche stumm
        /// wirkungslos machen, und niemand rechnet damit.
        /// </remarks>
        public static bool ShouldRemember(DialogKey key, bool isCheckBoxChecked, bool isAffirmative) =>
            key != DialogKey.None && isCheckBoxChecked && isAffirmative;
    }
}
