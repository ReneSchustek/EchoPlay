using EchoPlay.Logger.Models;
using EchoPlay.Logger.Tests.Fakes;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für die <see cref="EchoPlay.Logger.Core.Logger"/>-Implementierung.
    /// Prüft Level-Filterung, Sink-Weiterleitung, Eintragsinhalte und Scope-Integration.
    /// </summary>
    public sealed class LoggerTests
    {
        // ===== Level-Filterung =====

        /// <summary>
        /// Eine Debug-Nachricht wird bei Minimum-Level Trace weitergeleitet.
        /// </summary>
        [Fact]
        public void Debug_MinimumLevelIsTrace_ForwardsToSink()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Trace });

            logger.Debug("Testmeldung");

            _ = Assert.Single(sink.Entries);
        }

        /// <summary>
        /// Eine Trace-Nachricht wird bei Minimum-Level Debug gefiltert.
        /// </summary>
        [Fact]
        public void Trace_BeiMinimumDebug_WirdGefiltert()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Trace("Trace-Meldung");

            Assert.Empty(sink.Entries);
        }

        /// <summary>
        /// Eine Debug-Nachricht wird bei Minimum-Level Information gefiltert.
        /// </summary>
        [Fact]
        public void Debug_BeiMinimumInformation_WirdGefiltert()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Information });

            logger.Debug("Debug-Meldung");

            Assert.Empty(sink.Entries);
        }

        // ===== Lazy-Overload: Func<string>-Variante allokiert Message nur, wenn Level aktiv =====

        [Fact]
        public void Debug_WithFactory_DoesNotInvokeWhenDisabled()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Information });
            int factoryCalls = 0;

            logger.Debug(() => { factoryCalls++; return "Teure Nachricht"; });

            Assert.Equal(0, factoryCalls);
            Assert.Empty(sink.Entries);
        }

        [Fact]
        public void Debug_WithFactory_InvokesWhenEnabled()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });
            int factoryCalls = 0;

            logger.Debug(() => { factoryCalls++; return "Teure Nachricht"; });

            Assert.Equal(1, factoryCalls);
            _ = Assert.Single(sink.Entries);
            Assert.Equal("Teure Nachricht", sink.Entries[0].Message);
        }

        [Fact]
        public void Debug_WithFactory_NullFactory_Throws()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            _ = Assert.Throws<ArgumentNullException>(() => logger.Debug((Func<string>)null!));
        }

        [Fact]
        public void IsDebugEnabled_VariesWithMinimumLevel_DebugVsInformation()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logDebug = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });
            EchoPlay.Logger.Core.Logger logInfo = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Information });

            Assert.True(logDebug.IsDebugEnabled);
            Assert.False(logInfo.IsDebugEnabled);
        }

        /// <summary>
        /// Eine Info-Nachricht wird bei Minimum-Level Warning gefiltert.
        /// </summary>
        [Fact]
        public void Information_BeiMinimumWarning_WirdGefiltert()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Warning });

            logger.Info("Info-Meldung");

            Assert.Empty(sink.Entries);
        }

        /// <summary>
        /// Eine Warning-Nachricht wird bei Minimum-Level Error gefiltert.
        /// </summary>
        [Fact]
        public void Warning_BeiMinimumError_WirdGefiltert()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Error });

            logger.Warning("Warnung");

            Assert.Empty(sink.Entries);
        }

        /// <summary>
        /// Eine Error-Nachricht wird bei Minimum-Level Fatal gefiltert.
        /// </summary>
        [Fact]
        public void Error_BeiMinimumFatal_WirdGefiltert()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Fatal });

            logger.Error("Fehlermeldung");

            Assert.Empty(sink.Entries);
        }

        /// <summary>
        /// Eine Fatal-Nachricht wird bei Minimum-Level Fatal weitergeleitet.
        /// </summary>
        [Fact]
        public void Fatal_BeiMinimumFatal_WirdWeitergeleitet()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Fatal });

            logger.Fatal("Kritischer Fehler");

            _ = Assert.Single(sink.Entries);
        }

        /// <summary>
        /// Alle sechs Log-Methoden produzieren bei Minimum-Level Trace je einen Eintrag
        /// und die Einträge besitzen die korrekten Log-Level.
        /// </summary>
        [Fact]
        public void AlleLevel_AbMinimumTrace_WerdenWeitergeleitet()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Trace });

            logger.Trace("Trace");
            logger.Debug("Debug");
            logger.Info("Info");
            logger.Warning("Warning");
            logger.Error("Error");
            logger.Fatal("Fatal");

            Assert.Equal(6, sink.Entries.Count);
            Assert.Equal(LogLevel.Trace, sink.Entries[0].Level);
            Assert.Equal(LogLevel.Debug, sink.Entries[1].Level);
            Assert.Equal(LogLevel.Information, sink.Entries[2].Level);
            Assert.Equal(LogLevel.Warning, sink.Entries[3].Level);
            Assert.Equal(LogLevel.Error, sink.Entries[4].Level);
            Assert.Equal(LogLevel.Fatal, sink.Entries[5].Level);
        }

        // ===== Sink-Weiterleitung =====

        /// <summary>
        /// Ein Logger ohne registrierte Senken wirft keine Exception.
        /// </summary>
        [Fact]
        public void OhneSinks_WirftKeineException()
        {
            EchoPlay.Logger.Core.Logger logger = new("Kat", [], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            // Keine Exception erwartet
            logger.Debug("Meldung ohne Senke");
        }

        /// <summary>
        /// Mehrere Senken erhalten denselben Eintrag.
        /// </summary>
        [Fact]
        public void MehrereSinks_AlleErhaltenDenselbenEintrag()
        {
            CapturingSink sink1 = new();
            CapturingSink sink2 = new();
            CapturingSink sink3 = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink1, sink2, sink3], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Info("Meldung an alle");

            _ = Assert.Single(sink1.Entries);
            _ = Assert.Single(sink2.Entries);
            _ = Assert.Single(sink3.Entries);
            Assert.Equal("Meldung an alle", sink1.Entries[0].Message);
            Assert.Equal("Meldung an alle", sink2.Entries[0].Message);
            Assert.Equal("Meldung an alle", sink3.Entries[0].Message);
        }

        /// <summary>
        /// Eine fehlerhafte Senke blockiert nicht die nachfolgenden Senken
        /// und propagiert die Exception nicht an den Aufrufer.
        /// </summary>
        [Fact]
        public void SinkMitException_WirdAbgefangen_NachfolgendeSinksErhaltenEintrag()
        {
            ThrowingSink throwingSink = new();
            CapturingSink capturingSink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [throwingSink, capturingSink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            // Keine Exception erwartet – Logger fängt Senken-Fehler intern ab
            logger.Info("Meldung mit fehlerhafter Senke");

            // Die funktionierende Senke hat den Eintrag dennoch erhalten
            _ = Assert.Single(capturingSink.Entries);
        }

        // ===== Eintrag-Inhalt =====

        /// <summary>
        /// Der Log-Eintrag enthält die korrekte Kategorie des Loggers.
        /// </summary>
        [Fact]
        public void Info_EnthältKorrektKategorie()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("MeineKlasse", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Info("Meldung");

            Assert.Equal("MeineKlasse", sink.Entries[0].Category);
        }

        /// <summary>
        /// Der Log-Eintrag enthält die ursprüngliche Nachricht unverändert.
        /// </summary>
        [Fact]
        public void Info_EnthältNachrichtUnverändert()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Info("Testmeldung mit Sonderzeichen: äöüß");

            Assert.Equal("Testmeldung mit Sonderzeichen: äöüß", sink.Entries[0].Message);
        }

        /// <summary>
        /// Der Log-Eintrag enthält das korrekte Log-Level.
        /// </summary>
        [Fact]
        public void Info_EnthältKorrektemLevel()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Info("Meldung");

            Assert.Equal(LogLevel.Information, sink.Entries[0].Level);
        }

        /// <summary>
        /// Ein Error-Eintrag mit Exception enthält die übergebene Exception-Instanz.
        /// </summary>
        [Fact]
        public void Error_MitException_ExceptionImEintrag()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });
            InvalidOperationException exception = new("Testfehler");

            logger.Error("Fehlermeldung", exception);

            Assert.Same(exception, sink.Entries[0].Exception);
        }

        /// <summary>
        /// Ein Error-Eintrag ohne Exception hat null als Exception-Eigenschaft.
        /// </summary>
        [Fact]
        public void Error_OhneException_ExceptionIstNull()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Error("Fehlermeldung ohne Exception");

            Assert.Null(sink.Entries[0].Exception);
        }

        /// <summary>
        /// Ein Fatal-Eintrag mit Exception enthält Level Fatal und die übergebene Exception.
        /// </summary>
        [Fact]
        public void Fatal_MitException_LevelUndExceptionKorrekt()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });
            InvalidOperationException exception = new("Kritischer Zustand");

            logger.Fatal("Fatale Meldung", exception);

            Assert.Equal(LogLevel.Fatal, sink.Entries[0].Level);
            Assert.Same(exception, sink.Entries[0].Exception);
        }

        // ===== Scopes =====

        /// <summary>
        /// Ohne aktiven Scope ist die Scope-Liste im Log-Eintrag leer.
        /// </summary>
        [Fact]
        public void Debug_OhneScope_ScopelisteIstLeer()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            logger.Debug("Meldung ohne Scope");

            Assert.Empty(sink.Entries[0].Scopes);
        }

        /// <summary>
        /// Ein aktiver Scope erscheint in der Scope-Liste des Log-Eintrags.
        /// </summary>
        [Fact]
        public void Debug_MitAktivenScope_ScopeImEintrag()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            using (EchoPlay.Logger.Scoping.LogScope scope = logger.BeginScope("API:Test:Vorgang"))
            {
                logger.Debug("Meldung im Scope");
            }

            Assert.Contains("API:Test:Vorgang", sink.Entries[0].Scopes);
        }

        /// <summary>
        /// Nach Beenden des Scopes enthält der nächste Log-Eintrag keine Scope-Informationen mehr.
        /// </summary>
        [Fact]
        public void Debug_NachScopeDispose_KeinScopeImNächstenEintrag()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            using (EchoPlay.Logger.Scoping.LogScope scope = logger.BeginScope("Temporärer Scope"))
            {
                logger.Debug("Meldung im Scope");
            }

            logger.Debug("Meldung außerhalb des Scopes");

            Assert.Equal(2, sink.Entries.Count);
            Assert.Empty(sink.Entries[1].Scopes);
        }

        /// <summary>
        /// Zwei geschachtelte Scopes erscheinen beide in der Scope-Liste des Log-Eintrags.
        /// </summary>
        [Fact]
        public void Debug_MitGeschachteltenScopes_BeideScopes()
        {
            CapturingSink sink = new();
            EchoPlay.Logger.Core.Logger logger = new("Kat", [sink], new Configuration.LoggerOptions { MinimumLevel = LogLevel.Debug });

            using (EchoPlay.Logger.Scoping.LogScope outer = logger.BeginScope("Äußerer"))
            {
                using (EchoPlay.Logger.Scoping.LogScope inner = logger.BeginScope("Innerer"))
                {
                    logger.Debug("Meldung mit zwei Scopes");
                }
            }

            IReadOnlyList<string> scopes = sink.Entries[0].Scopes;
            Assert.Equal(2, scopes.Count);
            Assert.Contains("Äußerer", scopes);
            Assert.Contains("Innerer", scopes);
        }
    }
}
