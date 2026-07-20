using System;
using System.Linq;
using Xunit;

namespace EchoPlay.Core.Tests.Architecture
{
    /// <summary>
    /// Architektur-Guard: EchoPlay.Core ist die innerste Schicht. Sie darf auf kein
    /// anderes EchoPlay-Modul verweisen — einzige erlaubte EchoPlay-Referenz ist
    /// <c>EchoPlay.Logger.Abstractions</c>. Der Test friert diese Regel ein, damit ein
    /// versehentlicher Rück-Verweis (z.B. Core → Data/App) sofort rot wird.
    /// </summary>
    public sealed class CoreDependencyRulesTests
    {
        [Fact]
        public void Core_ReferencesNoEchoPlayModule_ExceptLoggerAbstractions()
        {
            // Anker in EchoPlay.Core; über das Assembly kommen wir an die referenzierten Assemblys.
            System.Reflection.Assembly coreAssembly = typeof(AudioExtensions).Assembly;

            string[] echoPlayReferences = coreAssembly
                .GetReferencedAssemblies()
                .Select(name => name.Name ?? string.Empty)
                .Where(name => name.StartsWith("EchoPlay.", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Genau eine EchoPlay-Referenz erlaubt — und das muss Logger.Abstractions sein.
            string onlyReference = Assert.Single(echoPlayReferences);
            Assert.Equal("EchoPlay.Logger.Abstractions", onlyReference);
        }
    }
}
