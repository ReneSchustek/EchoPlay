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
        public async Task<byte[]?> GetAsync(string key)
        {
            SecureSetting? setting = await _context.SecureSettings
                .FirstOrDefaultAsync(s => s.Key == key)
                .ConfigureAwait(false);

            return setting?.EncryptedValue;
        }

        /// <inheritdoc/>
        public async Task SaveAsync(string key, byte[] encryptedValue)
        {
            SecureSetting? existing = await _context.SecureSettings
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Key == key)
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

            await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"Verschlüsselter Wert für '{key}' gespeichert.");
        }

        /// <inheritdoc/>
        public async Task DeleteAsync(string key)
        {
            SecureSetting? existing = await _context.SecureSettings
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Key == key)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                _ = _context.SecureSettings.Remove(existing);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                _logger.Info($"Verschlüsselter Wert für '{key}' gelöscht.");
            }
        }
    }
}
