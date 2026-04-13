using EchoPlay.Data.Context;
using EchoPlay.Data.Tests.Helper;
using System.Diagnostics.CodeAnalysis;

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
    [SuppressMessage("Design", "CA1515:Consider making public types internal",
        Justification = "Basisklasse für xUnit-Test-Klassen, die public sein müssen; public ist konsistent mit den Ableitungen.")]
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
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gibt die für den Test gehaltenen Ressourcen frei.
        /// Die explizite Freigabe des DbContext ist erforderlich, da SQLite-InMemory-Datenbanken an die Lebensdauer der Verbindung
        /// gebunden sind und andernfalls unerwartete Seiteneffekte auftreten können.
        /// </summary>
        /// <param name="disposing"><c>true</c>, wenn verwaltete Ressourcen freigegeben werden sollen.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Context.Dispose();
            }
        }
    }
}