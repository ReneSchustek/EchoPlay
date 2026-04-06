using EchoPlay.Logger.Scoping;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für <see cref="LogScope"/> und <see cref="LogScopeManager"/>.
    /// Prüft korrektes Push- und Pop-Verhalten des Scope-Stacks sowie Reihenfolge-Durchsetzung.
    /// </summary>
    public sealed class LogScopeTests
    {
        /// <summary>
        /// Ohne aktiven Scope ist CurrentScopes leer.
        /// </summary>
        [Fact]
        public void CurrentScopes_OhneAktivenScope_IstLeer()
        {
            Assert.Empty(LogScopeManager.CurrentScopes);
        }

        /// <summary>
        /// Nach Erstellen eines Scopes ist er in CurrentScopes sichtbar.
        /// </summary>
        [Fact]
        public void BeginScope_ActiviertScope_SichtbarInCurrentScopes()
        {
            using (LogScope scope = new("Testscope"))
            {
                Assert.Contains("Testscope", LogScopeManager.CurrentScopes);
            }
        }

        /// <summary>
        /// Nach dem using-Block ist der Scope nicht mehr in CurrentScopes.
        /// </summary>
        [Fact]
        public void Dispose_EntferntScope_NichtMehrSichtbar()
        {
            using (LogScope scope = new("Scope-A"))
            {
                // Scope aktiv
            }

            Assert.DoesNotContain("Scope-A", LogScopeManager.CurrentScopes);
        }

        /// <summary>
        /// Zwei geschachtelte Scopes sind beide gleichzeitig sichtbar.
        /// </summary>
        [Fact]
        public void GeschachtelteScopes_BeideSichtbar()
        {
            using (LogScope outer = new("Äußerer"))
            {
                using (LogScope inner = new("Innerer"))
                {
                    Assert.Contains("Äußerer", LogScopeManager.CurrentScopes);
                    Assert.Contains("Innerer", LogScopeManager.CurrentScopes);
                }
            }
        }

        /// <summary>
        /// Nach Beenden des inneren Scopes ist nur noch der äußere sichtbar.
        /// </summary>
        [Fact]
        public void InnerenScopeDispose_NurÄußererBleibt()
        {
            using (LogScope outer = new("Äußerer"))
            {
                using (LogScope inner = new("Innerer"))
                {
                    // Beide aktiv
                }

                Assert.Contains("Äußerer", LogScopeManager.CurrentScopes);
                Assert.DoesNotContain("Innerer", LogScopeManager.CurrentScopes);
            }
        }

        /// <summary>
        /// Nach Beenden beider Scopes ist CurrentScopes wieder leer.
        /// </summary>
        [Fact]
        public void BeideScopesDispose_CurrentScopesIstLeer()
        {
            using (LogScope outer = new("Äußerer"))
            {
                using (LogScope inner = new("Innerer"))
                {
                    // Beide aktiv
                }
            }

            Assert.Empty(LogScopeManager.CurrentScopes);
        }

        /// <summary>
        /// Die Reihenfolge in CurrentScopes entspricht der Öffnungsreihenfolge (ältester Scope zuerst).
        /// </summary>
        [Fact]
        public void GeschachtelteScopes_ReihenfolgeKorrekt_ÄltesterZuerst()
        {
            using (LogScope outer = new("Erster"))
            {
                using (LogScope inner = new("Zweiter"))
                {
                    IReadOnlyList<string> scopes = LogScopeManager.CurrentScopes;

                    Assert.Equal(2, scopes.Count);
                    Assert.Equal("Erster", scopes[0]);
                    Assert.Equal("Zweiter", scopes[1]);
                }
            }
        }

        /// <summary>
        /// Drei geschachtelte Scopes sind alle in korrekter Öffnungsreihenfolge sichtbar.
        /// </summary>
        [Fact]
        public void DreiGeschachtelteScopes_AlleInKorrekterReihenfolge()
        {
            using (LogScope s1 = new("Ebene1"))
            {
                using (LogScope s2 = new("Ebene2"))
                {
                    using (LogScope s3 = new("Ebene3"))
                    {
                        IReadOnlyList<string> scopes = LogScopeManager.CurrentScopes;

                        Assert.Equal(3, scopes.Count);
                        Assert.Equal("Ebene1", scopes[0]);
                        Assert.Equal("Ebene2", scopes[1]);
                        Assert.Equal("Ebene3", scopes[2]);
                    }
                }
            }
        }

        /// <summary>
        /// Zwei sequentielle (nicht geschachtelte) Scopes sind voneinander unabhängig.
        /// Zwischen beiden Scopes ist CurrentScopes leer.
        /// </summary>
        [Fact]
        public void SequentielleScopes_UnabhängigVoneinander()
        {
            using (LogScope first = new("Erster"))
            {
                Assert.Single(LogScopeManager.CurrentScopes);
                Assert.Equal("Erster", LogScopeManager.CurrentScopes[0]);
            }

            // Zwischen beiden Scopes: keine aktiven Scopes
            Assert.Empty(LogScopeManager.CurrentScopes);

            using (LogScope second = new("Zweiter"))
            {
                Assert.Single(LogScopeManager.CurrentScopes);
                Assert.Equal("Zweiter", LogScopeManager.CurrentScopes[0]);
            }
        }

        /// <summary>
        /// Das Dispose eines äußeren Scopes vor dem inneren verletzt die LIFO-Reihenfolge.
        /// <see cref="LogScope.Dispose"/> wirft in diesem Fall keine Exception – Exceptions aus
        /// Dispose() würden eine bereits aktive Exception überdecken und die Fehlerdiagnose erschweren.
        /// Der Stack-Fehler wird stattdessen über <see cref="System.Diagnostics.Trace.WriteLine"/> gemeldet.
        /// </summary>
        [Fact]
        public void ScopeDisposeInVerkehrterReihenfolge_WirftKeine_Exception()
        {
            LogScope outer = new("Äußerer");
            LogScope inner = new("Innerer");

            try
            {
                // Äußeren Scope zu schließen während innerer noch offen ist: kein Throw,
                // da Dispose() nie werfen darf (würde laufende Exceptions überschreiben).
                Exception? thrown = Record.Exception(() => outer.Dispose());
                Assert.Null(thrown);
            }
            finally
            {
                // Stack bereinigen, damit nachfolgende Tests nicht beeinflusst werden
                inner.Dispose();
                outer.Dispose();
            }
        }
    }
}
