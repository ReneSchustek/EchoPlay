using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung für verschlüsselte Einstellungswerte.
    /// </summary>
    public sealed class SecureSettingsDataService(
        EchoPlayDbContext context,
        ILoggerFactory loggerFactory) : ISecureSettingsDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly ILogger _logger = loggerFactory.CreateLogger("SecureSettingsDataService");

        /// <inheritdoc/>
        /// <param name="key">Parameter key.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            SecureSetting? setting = await _context.SecureSettings
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
                .ConfigureAwait(false);

            return setting?.EncryptedValue;
        }

        /// <inheritdoc/>
        /// <param name="key">Parameter key.</param>
        /// <param name="encryptedValue">Parameter encryptedValue.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task SaveAsync(string key, byte[] encryptedValue, CancellationToken cancellationToken = default)
        {
            SecureSetting? existing = await _context.SecureSettings
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                existing.EncryptedValue = encryptedValue;
            }
            else
            {
                _ = _context.SecureSettings.Add(new SecureSetting
                {
                    Key = key,
                    EncryptedValue = encryptedValue
                });
            }

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"Verschlüsselter Wert für '{key}' gespeichert.");
        }

        /// <inheritdoc/>
        /// <param name="key">Parameter key.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            SecureSetting? existing = await _context.SecureSettings
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                _ = _context.SecureSettings.Remove(existing);
                _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info($"Verschlüsselter Wert für '{key}' gelöscht.");
            }
        }
    }
}
