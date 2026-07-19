using System.Collections.Immutable;

namespace EchoPlay.Logger.Scoping
{
    /// <summary>
    /// Verwaltet den aktuellen Scope-Stack für alle Log-Operationen.
    /// </summary>
    public static class LogScopeManager
    {
        private readonly static AsyncLocal<ImmutableStack<string>> _scopeStack = new();

        /// <summary>
        /// Gibt die aktuell aktiven Scopes in chronologischer Reihenfolge zurück.
        /// </summary>
        public static IReadOnlyList<string> CurrentScopes => _scopeStack.Value?.Reverse().ToImmutableList() ?? [];

        /// <summary>
        /// Legt einen neuen Scope auf den Stack.
        /// </summary>
        /// <param name="name">Name des Scopes.</param>
        internal static void PushScope(string name)
        {
            ImmutableStack<string> currentStack = _scopeStack.Value ?? [];
            _scopeStack.Value = currentStack.Push(name);
        }

        /// <summary>
        /// Entfernt den angegebenen Scope vom Stack.
        /// </summary>
        /// <param name="name">Name des Scopes, der entfernt werden soll.</param>
        /// <exception cref="InvalidOperationException">
        /// Wird geworfen, wenn der Stack leer ist oder der oberste Scope nicht übereinstimmt.
        /// </exception>
        internal static void PopScope(string name)
        {
            if (_scopeStack.Value == null || _scopeStack.Value.IsEmpty)
            {
                throw new InvalidOperationException("Keine Scopes zum Entfernen vorhanden.");
            }

            if (_scopeStack.Value.Peek() != name)
            {
                throw new InvalidOperationException(
                    $"Scope-Reihenfolge verletzt: Erwartet '{_scopeStack.Value.Peek()}', erhalten '{name}'.");
            }

            _scopeStack.Value = _scopeStack.Value.Pop();
        }
    }
}
