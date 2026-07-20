using System.Diagnostics.CodeAnalysis;
using EchoPlay.Data.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Verifiziert, dass <see cref="UniqueConstraintHandler"/> ausschließlich
    /// <c>SQLITE_CONSTRAINT_UNIQUE</c> (Extended-Code 2067) als Race-Condition
    /// behandelt. Alle anderen Constraint-Verletzungen müssen propagiert werden,
    /// damit echte Datenintegritätsfehler sichtbar bleiben.
    /// </summary>
    public sealed class UniqueConstraintHandlerTests
    {
        /// <summary>
        /// Baut eine <see cref="SqliteException"/> über einen echten SQLite-Round-Trip,
        /// damit der <see cref="SqliteException.SqliteExtendedErrorCode"/> korrekt gefüllt ist.
        /// Das Konstruktor-API von <see cref="SqliteException"/> erlaubt keine direkte
        /// Angabe des Extended-Codes, deshalb provozieren wir die Fehler gegen eine
        /// In-Memory-Datenbank.
        /// </summary>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "Test-Helfer: SQL-Strings sind Literale direkt im Testcode, keine externen Eingaben.")]
        private static SqliteException ProvokeSqliteException(string setupSql, string failingSql)
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using (SqliteCommand setup = connection.CreateCommand())
            {
                setup.CommandText = setupSql;
                _ = setup.ExecuteNonQuery();
            }

            using SqliteCommand failing = connection.CreateCommand();
            failing.CommandText = failingSql;
            try
            {
                _ = failing.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                return ex;
            }

            throw new InvalidOperationException("SQLite hat erwartete Constraint-Verletzung nicht ausgelöst.");
        }

        [Fact]
        public void IsUniqueViolation_ReturnsTrue_ForUniqueConstraint()
        {
            SqliteException inner = ProvokeSqliteException(
                "CREATE TABLE T (Id INTEGER PRIMARY KEY, Key TEXT UNIQUE); INSERT INTO T VALUES (1, 'a');",
                "INSERT INTO T VALUES (2, 'a');");

            Assert.Equal(2067, inner.SqliteExtendedErrorCode);

            DbUpdateException ex = new("wrapped", inner);
            Assert.True(UniqueConstraintHandler.IsUniqueViolation(ex));
        }

        [Fact]
        public void IsUniqueViolation_ReturnsFalse_ForNotNullConstraint()
        {
            SqliteException inner = ProvokeSqliteException(
                "CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);",
                "INSERT INTO T (Id, Name) VALUES (1, NULL);");

            Assert.NotEqual(2067, inner.SqliteExtendedErrorCode);

            DbUpdateException ex = new("wrapped", inner);
            Assert.False(UniqueConstraintHandler.IsUniqueViolation(ex));
        }

        [Fact]
        public void IsUniqueViolation_ReturnsFalse_ForForeignKeyConstraint()
        {
            SqliteException inner = ProvokeSqliteException(
                "PRAGMA foreign_keys = ON;" +
                "CREATE TABLE Parent (Id INTEGER PRIMARY KEY);" +
                "CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES Parent(Id));",
                "INSERT INTO Child (Id, ParentId) VALUES (1, 99);");

            Assert.NotEqual(2067, inner.SqliteExtendedErrorCode);

            DbUpdateException ex = new("wrapped", inner);
            Assert.False(UniqueConstraintHandler.IsUniqueViolation(ex));
        }

        [Fact]
        public void IsUniqueViolation_ReturnsFalse_ForCheckConstraint()
        {
            SqliteException inner = ProvokeSqliteException(
                "CREATE TABLE T (Id INTEGER PRIMARY KEY, Amount INTEGER CHECK (Amount >= 0));",
                "INSERT INTO T (Id, Amount) VALUES (1, -5);");

            Assert.NotEqual(2067, inner.SqliteExtendedErrorCode);

            DbUpdateException ex = new("wrapped", inner);
            Assert.False(UniqueConstraintHandler.IsUniqueViolation(ex));
        }

        [Fact]
        public void IsUniqueViolation_ReturnsFalse_ForNonSqliteInner()
        {
            DbUpdateException ex = new("wrapped", new InvalidOperationException("nicht-SQLite"));
            Assert.False(UniqueConstraintHandler.IsUniqueViolation(ex));
        }

        [Fact]
        public void IsUniqueViolation_ReturnsFalse_ForNullInner()
        {
            DbUpdateException ex = new("wrapped");
            Assert.False(UniqueConstraintHandler.IsUniqueViolation(ex));
        }
    }
}
