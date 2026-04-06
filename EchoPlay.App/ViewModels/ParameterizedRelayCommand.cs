using System;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// <see cref="ICommand"/>-Implementierung, die eine Aktion mit Parameter kapselt.
    /// Wird verwendet, wenn derselbe Command mit unterschiedlichen Werten aufgerufen werden soll –
    /// z.B. Theme-Wechsel mit dem Theme-Namen als Parameter.
    ///
    /// WinUI 3 übergibt den <c>CommandParameter</c> des auslösenden Elements als <see langword="object"/>
    /// an <see cref="Execute"/>. Der Aufrufer ist verantwortlich für das Casting.
    /// </summary>
    public sealed class ParameterizedRelayCommand : ICommand
    {
        private readonly Action<object?> _execute;

        /// <summary>
        /// Wird ausgelöst, wenn sich <see cref="CanExecute"/> geändert hat.
        /// Für dieses Command immer <see langword="true"/> – das Event wird nie gefeuert.
        /// </summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// Initialisiert das Command mit der auszuführenden Aktion.
        /// </summary>
        /// <param name="execute">Die auszuführende Aktion. Erhält den CommandParameter als Argument.</param>
        public ParameterizedRelayCommand(Action<object?> execute)
        {
            _execute = execute;
        }

        /// <inheritdoc/>
        /// <returns>Immer <see langword="true"/> – das Command ist stets ausführbar.</returns>
        public bool CanExecute(object? parameter) => true;

        /// <inheritdoc/>
        public void Execute(object? parameter) => _execute(parameter);

        /// <summary>
        /// Löst <see cref="CanExecuteChanged"/> aus, falls der Aktivierungszustand extern gesteuert werden soll.
        /// Bei diesem Command ist der Zustand immer aktiv – der Parameter wird ignoriert.
        /// </summary>
        /// <param name="_">Nicht verwendet.</param>
        public void SetEnabled(bool _)
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
