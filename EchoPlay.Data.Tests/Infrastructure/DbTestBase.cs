using EchoPlay.Data.Context;
using EchoPlay.Data.Tests.Helper;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Basisklasse für alle datenbankbasierten Tests im Projekt.
    /// Sie stellt einen isolierten DbContext sowie zentrale Hilfsmittel
    /// für den Aufbau konsistenter Testdaten bereit.
    ///
    /// Diese Klasse bildet bewusst den technischen Rahmen für Tests
    /// und kapselt alle Infrastrukturdetails, um die einzelnen Tests
    /// auf fachliches Verhalten fokussieren zu können.
    /// </summary>
    public abstract class DbTestBase : IDisposable
    {
        /// <summary>
        /// Null-Logger-Factory für Tests, die einen Logger-Parameter benötigen, aber keine Ausgabe erwarten.
        /// </summary>
        protected static readonly EchoPlay.Logger.Abstractions.ILoggerFactory NullLoggerFactory =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        /// <summary>
        /// Der für den jeweiligen Test gültige Datenbankkontext.
        /// Der Context ist vollständig isoliert und wird pro Testklasse neu erstellt, um Seiteneffekte zwischen Tests zu vermeiden.
        /// </summary>
        protected EchoPlayDbContext Context { get; }

        /// <summary>
        /// Hilfsobjekt zum expliziten Erzeugen und Persistieren valider Testdaten unter Einhaltung der notwendigen Foreign-Key-Reihenfolge.
        /// Der Builder verhindert implizite Objektgraphen und stellt sicher, dass Tests reale Datenbankzustände widerspiegeln.
        /// </summary>
        protected TestDataBuilder DataBuilder { get; }

        /// <summary>
        /// Initialisiert den Testkontext und alle zugehörigen Hilfsmittel.
        /// Die Initialisierung erfolgt bewusst im Konstruktor, da jede Testklasse einen eigenen, vollständig isolierten Kontext
        /// benötigt, um reproduzierbare Ergebnisse zu gewährleisten.
        /// </summary>
        protected DbTestBase()
        {
            Context = SqliteInMemoryDbContextFactory.Create();
            DataBuilder = new(Context);
        }

        /// <summary>
        /// Gibt alle vom Test verwendeten Ressourcen frei.
        /// Die explizite Freigabe des DbContext ist erforderlich, da SQLite-InMemory-Datenbanken an die Lebensdauer der Verbindung
        /// gebunden sind und andernfalls unerwartete Seiteneffekte auftreten können.
        /// </summary>
        public void Dispose()
        {
            Context.Dispose();

            // Unterdrückt den Finalizer, da keine unmanaged Ressourcen
            // außerhalb des DbContext verwaltet werden.
            GC.SuppressFinalize(this);
        }
    }
}