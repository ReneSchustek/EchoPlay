using System.Runtime.InteropServices;
using System.Text;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace EchoPlay.Benchmarks.Cases.Reports
{
    /// <summary>
    /// Liest den Plan, den SQLite für einen Befehl wählt, ohne den Befehlstext zu verändern.
    /// </summary>
    /// <remarks>
    /// Der naheliegende Weg ist, <c>EXPLAIN QUERY PLAN</c> vor den Befehl zu setzen. Das ergibt
    /// einen zusammengesetzten Befehlstext, und genau den meldet die Regel CA2100 zu Recht: Ob der
    /// angehängte Teil vertrauenswürdig ist, sieht ihm niemand an. SQLite bietet dafür seit 3.43
    /// <c>sqlite3_stmt_explain</c>: Die Anweisung wird unverändert vorbereitet und dann in den
    /// Planmodus geschaltet. Microsoft.Data.Sqlite und SQLitePCLRaw reichen die Funktion nicht durch,
    /// deshalb der direkte Aufruf in die mitgelieferte <c>e_sqlite3</c>.
    /// </remarks>
    internal static class SqliteExplain
    {
        /// <summary>Planmodus von <c>sqlite3_stmt_explain</c>: wie <c>EXPLAIN QUERY PLAN</c>.</summary>
        private const int QueryPlanMode = 2;

        /// <summary>
        /// Liefert den Plan eines Befehls, eingerückt nach seiner Baumstruktur.
        /// </summary>
        /// <param name="databasePath">Die Datei, gegen die der Befehl läuft.</param>
        /// <param name="command">Der mitgeschnittene Befehl samt Parametern.</param>
        /// <returns>Eine Zeile je Planschritt.</returns>
        public static string QueryPlan(string databasePath, CapturedCommand command)
        {
            using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
            connection.Open();

            Check(raw.sqlite3_prepare_v2(connection.Handle, command.CommandText, out sqlite3_stmt statement), connection);
            using (statement)
            {
                Check(NativeMethods.sqlite3_stmt_explain(statement.DangerousGetHandle(), QueryPlanMode), connection);
                Bind(statement, command.Parameters);

                Dictionary<long, int> depth = [];
                StringBuilder plan = new();
                while (raw.sqlite3_step(statement) == raw.SQLITE_ROW)
                {
                    long id = raw.sqlite3_column_int64(statement, 0);
                    long parent = raw.sqlite3_column_int64(statement, 1);
                    int level = depth.TryGetValue(parent, out int parentLevel) ? parentLevel + 1 : 0;
                    depth[id] = level;
                    _ = plan.Append(' ', level * 2).AppendLine(raw.sqlite3_column_text(statement, 3).utf8_to_string());
                }

                return plan.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Bindet die Parameter mit ihren Werten. Der Plan hängt bei SQLite ohne erweiterte Statistik
        /// kaum von den Werten ab; gebunden wird trotzdem, damit der Befehl derselbe bleibt.
        /// </summary>
        private static void Bind(sqlite3_stmt statement, IReadOnlyList<KeyValuePair<string, object?>> parameters)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                int index = raw.sqlite3_bind_parameter_index(statement, parameter.Key);
                if (index == 0)
                {
                    continue;
                }

                _ = parameter.Value switch
                {
                    null or DBNull => raw.sqlite3_bind_null(statement, index),
                    byte[] bytes => raw.sqlite3_bind_blob(statement, index, bytes),
                    bool flag => raw.sqlite3_bind_int64(statement, index, flag ? 1 : 0),
                    int number => raw.sqlite3_bind_int64(statement, index, number),
                    long number => raw.sqlite3_bind_int64(statement, index, number),
                    double number => raw.sqlite3_bind_double(statement, index, number),
                    object other => raw.sqlite3_bind_text(statement, index, Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                };
            }
        }

        private static void Check(int result, SqliteConnection connection)
        {
            if (result != raw.SQLITE_OK)
            {
                throw new InvalidOperationException($"SQLite meldet {result}: {raw.sqlite3_errmsg(connection.Handle).utf8_to_string()}");
            }
        }

        /// <summary>
        /// Der direkte Aufruf in die SQLite-Bibliothek, die Microsoft.Data.Sqlite mitbringt.
        /// Sie ist beim Aufruf schon geladen, denn die Verbindung ist offen; der Name trifft dieselbe
        /// Bibliothek, gesucht wird nur über die Pfade aus den Abhängigkeiten der Anwendung.
        /// </summary>
        private static class NativeMethods
        {
            /// <summary>Schaltet eine vorbereitete Anweisung in den Plan- oder Erklärmodus.</summary>
            /// <param name="statement">Zeiger auf die vorbereitete Anweisung.</param>
            /// <param name="mode">0 normal, 1 wie <c>EXPLAIN</c>, 2 wie <c>EXPLAIN QUERY PLAN</c>.</param>
            /// <returns><c>SQLITE_OK</c> oder ein Fehlercode.</returns>
            [DllImport("e_sqlite3", CallingConvention = CallingConvention.Cdecl)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            internal static extern int sqlite3_stmt_explain(IntPtr statement, int mode);
        }
    }
}
