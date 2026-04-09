using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Basisklasse für alle ViewModels.
    /// Implementiert <see cref="INotifyPropertyChanged"/> und stellt
    /// <see cref="SetProperty{T}"/> bereit, das Änderungen nur dann feuert,
    /// wenn sich der Wert tatsächlich geändert hat.
    ///
    /// <see cref="INotifyPropertyChanged"/> ist der Vertrag zwischen ViewModel und UI:
    /// Wird das Event ausgelöst, weiß WinUI, dass es die gebundene Ansicht aktualisieren soll.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Setzt eine Property und löst <see cref="PropertyChanged"/> aus, wenn sich der Wert geändert hat.
        /// </summary>
        /// <typeparam name="T">Typ der Property.</typeparam>
        /// <param name="field">Das Backing-Field der Property.</param>
        /// <param name="value">Der neue Wert.</param>
        /// <param name="propertyName">
        /// Name der Property – wird durch <c>[CallerMemberName]</c> automatisch vom Compiler befüllt.
        /// Man muss den Namen nie als String tippen, was Tippfehler bei Bindings verhindert.
        /// </param>
        /// <returns><c>true</c> wenn der Wert geändert wurde, sonst <c>false</c>.</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // EqualityComparer<T>.Default verwendet den richtigen Vergleich für jeden Typ:
            // == für Primitives, Equals() für Objekte. Ohne diese Prüfung würde das Event
            // bei IsLoading = false; IsLoading = false; zweimal auslösen.
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Löst <see cref="PropertyChanged"/> für die angegebene Property aus.
        /// </summary>
        /// <param name="propertyName">Name der geänderten Property – wird automatisch gefüllt.</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
