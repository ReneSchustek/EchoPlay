using EchoPlay.Data.Services.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISecureSettingsDataService"/>. Speichert verschlüsselte Werte
    /// in einem Dictionary statt in der Datenbank.
    /// </summary>
    internal sealed class FakeSecureSettingsDataService : ISecureSettingsDataService
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public Task<byte[]?> GetAsync(string key)
        {
            _store.TryGetValue(key, out byte[]? value);
            return Task.FromResult(value);
        }

        public Task SaveAsync(string key, byte[] encryptedValue)
        {
            _store[key] = encryptedValue;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
    }
}
