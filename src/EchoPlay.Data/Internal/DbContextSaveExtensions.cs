using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Internal
{
    /// <summary>
    /// Speicher-Erweiterungen für den <see cref="EchoPlayDbContext"/>.
    /// </summary>
    internal static class DbContextSaveExtensions
    {
        /// <summary>
        /// Speichert die Änderungen und toleriert einen UNIQUE-Konflikt (paralleler Insert
        /// desselben fachlichen Schlüssels). Liefert bei Erfolg <c>null</c>, sonst die
        /// aufgetretene <see cref="DbUpdateException"/>, damit der Aufrufer sie protokollieren kann.
        /// </summary>
        /// <param name="context">Der Datenbankkontext.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><c>null</c> bei Erfolg; die Ausnahme bei toleriertem UNIQUE-Konflikt.</returns>
        public static async Task<DbUpdateException?> TrySaveChangesIgnoreUniqueAsync(
            this EchoPlayDbContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (DbUpdateException ex) when (UniqueConstraintHandler.IsUniqueViolation(ex))
            {
                return ex;
            }
        }
    }
}
