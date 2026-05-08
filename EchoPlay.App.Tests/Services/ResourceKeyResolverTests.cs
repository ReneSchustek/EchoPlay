using EchoPlay.App.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests fuer die Fallback-Logik des Ressourcen-Resolvers.
    /// Der Loader wird per Lambda gemockt, damit kein WinUI-ResourceLoader noetig ist.
    /// </summary>
    public sealed class ResourceKeyResolverTests
    {
        [Fact]
        public void Resolve_KeyExistsInLoader_ReturnsLoaderValue()
        {
            Dictionary<string, string> table = new() { ["NavStartseite"] = "Startseite" };

            string result = ResourceKeyResolver.Resolve("NavStartseite", k => table.GetValueOrDefault(k, string.Empty));

            Assert.Equal("Startseite", result);
        }

        [Fact]
        public void Resolve_KeyMissing_ReturnsMarkerWithKey()
        {
            string result = ResourceKeyResolver.Resolve("MissingKey", _ => string.Empty);

            Assert.Equal($"{ResourceKeyResolver.MissingKeyMarker}MissingKey", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_NullOrWhitespaceKey_ReturnsEmpty(string? key)
        {
            string result = ResourceKeyResolver.Resolve(key, _ => "shouldn't be called");

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Resolve_NullLoader_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => ResourceKeyResolver.Resolve("anything", null!));
        }
    }
}
