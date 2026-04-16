using EchoPlay.App.Services;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Scoping;
using EchoPlay.Logger.Sinks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreLogger = EchoPlay.Logger.Core.Logger;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="UserActionScope"/>. Prüft das äußere Scope-Format
    /// <c>UA:&lt;id&gt; &lt;name&gt;</c> sowie die Klammerung über alle Unter-Scopes hinweg.
    /// </summary>
    public sealed class UserActionScopeTests
    {
        /// <summary>
        /// Ersetzt den statischen ID-Generator für die Dauer des Tests und setzt ihn danach zurück,
        /// damit andere Tests unbeeinflusst bleiben.
        /// </summary>
        private sealed class IdGeneratorReplacement : IDisposable
        {
            private readonly Func<string> _previous;

            public IdGeneratorReplacement(string fixedId)
            {
                _previous = UserActionScope.IdGenerator;
                UserActionScope.IdGenerator = () => fixedId;
            }

            public void Dispose() => UserActionScope.IdGenerator = _previous;
        }

        [Fact]
        public void BeginUserAction_SchreibtUaPrefixUndNameInDenScopeStack()
        {
            using IdGeneratorReplacement idReset = new("a3f21c12");

            using IDisposable ua = UserActionScope.BeginUserAction("ImportSeries");

            IReadOnlyList<string> scopes = LogScopeManager.CurrentScopes;
            _ = Assert.Single(scopes);
            Assert.Equal("UA:a3f21c12 ImportSeries", scopes[0]);
        }

        [Fact]
        public void BeginUserAction_NachDispose_EntferntScopeVomStack()
        {
            using IdGeneratorReplacement idReset = new("cafebabe");

            using (UserActionScope.BeginUserAction("Play"))
            {
                _ = Assert.Single(LogScopeManager.CurrentScopes);
            }

            Assert.Empty(LogScopeManager.CurrentScopes);
        }

        [Fact]
        public void BeginUserAction_MitLeeremNamen_Wirft()
        {
            _ = Assert.Throws<ArgumentException>(() => UserActionScope.BeginUserAction(string.Empty));
            _ = Assert.Throws<ArgumentException>(() => UserActionScope.BeginUserAction("   "));
        }

        [Fact]
        public void BeginUserAction_StandardGenerator_LiefertAchtStelligeHexId()
        {
            using IDisposable ua = UserActionScope.BeginUserAction("Probe");

            string scope = LogScopeManager.CurrentScopes[0];
            Assert.StartsWith("UA:", scope, StringComparison.Ordinal);
            // Format: "UA:<8-hex> Probe"
            string[] parts = scope.Split(' ', 2);
            Assert.Equal(2, parts.Length);
            string idPart = parts[0]["UA:".Length..];
            Assert.Equal(8, idPart.Length);
            Assert.All(idPart, c => Assert.True(Uri.IsHexDigit(c)));
        }

        [Fact]
        public void BeginUserAction_ZweiAufrufe_ErzeugenUnterschiedlicheIds()
        {
            string first;
            string second;

            using (UserActionScope.BeginUserAction("A"))
            {
                first = LogScopeManager.CurrentScopes[0];
            }

            using (UserActionScope.BeginUserAction("B"))
            {
                second = LogScopeManager.CurrentScopes[0];
            }

            Assert.NotEqual(first, second);
        }

        /// <summary>
        /// Integrations-Test: Simuliert den Ablauf einer vollständigen User-Aktion
        /// (UA-Scope außen, zwei verschachtelte API-Scopes innen) und prüft, dass alle
        /// Log-Zeilen den UA-Prefix als äußersten Scope tragen – so wie es der Support
        /// per Log-Viewer-Filter erwartet.
        /// </summary>
        [Fact]
        public async Task LogZeilen_InnerhalbUserAction_TragenAlleDenSelbenUaPrefix()
        {
            using IdGeneratorReplacement idReset = new("deadbeef");

            MemorySink sink = new(capacity: 32);
            LoggerOptions options = new() { MinimumLevel = LogLevel.Debug };
            CoreLogger logger = new("ImportService", [sink], options);

            using (UserActionScope.BeginUserAction("ImportSeries"))
            {
                logger.Info("Import gestartet");
                using (logger.BeginScope("API:Spotify:SearchArtists"))
                {
                    logger.Info("Provider-Suche abgesetzt");
                    using (logger.BeginScope("HTTP:GET"))
                    {
                        logger.Debug("HTTP-Aufruf erfolgreich");
                    }
                }
                logger.Info("DB-Insert abgeschlossen");
            }

            // Logger.LogAsync läuft fire-and-forget – ein kurzer Yield reicht, weil MemorySink synchron schreibt.
            await Task.Yield();

            IReadOnlyList<LogEntry> entries = sink.GetEntries();
            Assert.Equal(4, entries.Count);

            foreach (LogEntry entry in entries)
            {
                Assert.NotEmpty(entry.Scopes);
                Assert.Equal("UA:deadbeef ImportSeries", entry.Scopes[0]);
            }

            // Die verschachtelten Scopes werden zusätzlich zum UA-Prefix geführt.
            Assert.Contains(entries, e => e.Message == "HTTP-Aufruf erfolgreich"
                && e.Scopes.Count == 3
                && e.Scopes[1] == "API:Spotify:SearchArtists"
                && e.Scopes[2] == "HTTP:GET");
        }

        /// <summary>
        /// Nach Dispose des UA-Scopes dürfen Folge-Logs den UA-Prefix nicht mehr tragen.
        /// </summary>
        [Fact]
        public async Task LogZeilen_NachDisposeDesUaScopes_TragenKeinenUaPrefixMehr()
        {
            using IdGeneratorReplacement idReset = new("12345678");

            MemorySink sink = new(capacity: 8);
            LoggerOptions options = new() { MinimumLevel = LogLevel.Debug };
            CoreLogger logger = new("TestCat", [sink], options);

            using (UserActionScope.BeginUserAction("Search"))
            {
                logger.Info("im Scope");
            }
            logger.Info("außerhalb");

            await Task.Yield();

            IReadOnlyList<LogEntry> entries = sink.GetEntries();
            LogEntry inside = entries[0];
            LogEntry outside = entries[1];

            Assert.Equal("UA:12345678 Search", Assert.Single(inside.Scopes));
            Assert.Empty(outside.Scopes);
        }
    }
}
