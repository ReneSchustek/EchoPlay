using System;
using System.Windows.Input;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Einfache <see cref="ICommand"/>-Implementierung, die eine Aktion als Delegate kapselt.
    /// Ausreichend für einmalige Aktionen ohne Parameter.
    ///
    /// <see cref="ICommand"/> ist der MVVM-Standardweg für Button-Aktionen: Das ViewModel stellt
    /// ein Command als Property bereit, die View bindet sich per <c>Command="{x:Bind ...}"</c> daran.
    /// Ist <see cref="CanExecute"/> <c>false</c>, deaktiviert WinUI den Button automatisch.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private bool _isEnabled = true;

        /// <summary>
        /// Wird ausgelöst, wenn sich <see cref="CanExecute"/> geändert hat.
        /// </summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// Initialisiert das Command mit der auszuführenden Aktion.
        /// </summary>
        /// <param name="execute">Die auszuführende Aktion.</param>
        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        /// <inheritdoc/>
        public bool CanExecute(object? parameter) => _isEnabled;

        /// <inheritdoc/>
        public void Execute(object? parameter) => _execute();

        /// <summary>
        /// Setzt den Aktivierungszustand und löst <see cref="CanExecuteChanged"/> aus.
        /// </summary>
        /// <param name="enabled">Ob das Command ausführbar sein soll.</param>
        public void SetEnabled(bool enabled)
        {
            if (_isEnabled == enabled)
            {
                return;
            }

            _isEnabled = enabled;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
