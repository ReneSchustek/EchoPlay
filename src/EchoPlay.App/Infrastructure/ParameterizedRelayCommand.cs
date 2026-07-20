using System;
using System.Windows.Input;

namespace EchoPlay.App.Infrastructure
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
        /// Von <see cref="ICommand"/> gefordert. <see cref="CanExecute"/> ist für dieses Command
        /// konstant <see langword="true"/>, daher gibt es keinen Zustandswechsel und die Accessoren
        /// sind bewusst leer.
        /// </summary>
        public event EventHandler? CanExecuteChanged { add { } remove { } }

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
    }
}
