using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Holt einen Protokollkanal für Ansichtsmodelle, die nur die Scope-Fabrik kennen. Sie werden
    /// teils von Hand gebaut und bekämen die Logger-Fabrik sonst nur über eine lange Kette von
    /// Konstruktoren durchgereicht.
    /// </summary>
    internal static class ScopedLogger
    {
        /// <summary>
        /// Erzeugt den Protokollkanal einmalig beim Aufbau; der Bereich wird danach sofort geschlossen,
        /// die Logger-Fabrik ist ein Singleton und überdauert ihn.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Fabrik des Ansichtsmodells.</param>
        /// <param name="name">Name des Kanals, üblicherweise der Klassenname.</param>
        /// <returns>Der Kanal, oder <see langword="null"/>, wenn keine Logger-Fabrik registriert ist (Tests ohne Protokoll).</returns>
        public static ILogger? Create(IServiceScopeFactory? scopeFactory, string name)
        {
            if (scopeFactory is null)
            {
                return null;
            }

            using IServiceScope scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(name);
        }
    }
}
