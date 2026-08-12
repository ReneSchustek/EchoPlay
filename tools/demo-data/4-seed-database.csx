#r "nuget: Microsoft.Data.Sqlite, 9.0.0"
#nullable enable

// Bereitet die Datenbank für die Aufnahmen vor: Bibliothekspfad, Hörstand, Favoriten,
// Neuerscheinungen.
//
// Läuft NACH dem ersten Einlesen der Bibliothek — die Hörstände hängen an Folgen, die es
// vorher nicht gibt. Die Anwendung muss dabei beendet sein, sonst ist die Datei gesperrt.
//
// Aufruf: dotnet script 4-seed-database.csx -- <Pfad zur echoplay.db> [Bibliothekswurzel]

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

string databasePath = Args.Count > 0
    ? Args[0]
    : Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\EchoPlay\echoplay.db");
string libraryRoot = Args.Count > 1 ? Args[1] : @"C:\Hörspiele-Demo";

// Fester Zeitpunkt statt DateTime.Now: Die Aufnahmen sollen bei jedem Lauf gleich aussehen.
DateTime reference = new(2026, 8, 11, 19, 30, 0, DateTimeKind.Utc);

var connection = new SqliteConnection($"Data Source={databasePath}");
connection.Open();

// --- 0. Schutz vor der echten Sammlung -------------------------------------------------
// Dieses Skript löscht Hörstände. Läuft es versehentlich gegen die private Datenbank, ist die
// Historie von Jahren weg. Die Demo-Bibliothek hat 20 Serien und 84 Folgen; alles, was
// deutlich darüber liegt, ist die echte Sammlung — dann wird nichts angefasst.
const int seriesLimit = 40;
const int episodeLimit = 300;

using (SqliteCommand guard = connection.CreateCommand())
{
    guard.CommandText = """
        SELECT (SELECT COUNT(*) FROM Series WHERE IsDeleted = 0),
               (SELECT COUNT(*) FROM Episodes WHERE IsDeleted = 0)
        """;
    using SqliteDataReader reader = guard.ExecuteReader();
    _ = reader.Read();
    int seriesTotal = reader.GetInt32(0);
    int episodeTotal = reader.GetInt32(1);

    if (seriesTotal > seriesLimit || episodeTotal > episodeLimit)
    {
        Console.WriteLine($"Abbruch: {seriesTotal} Serien und {episodeTotal} Folgen — das ist keine Demo-Bibliothek.");
        Console.WriteLine($"Erwartet werden höchstens {seriesLimit} Serien und {episodeLimit} Folgen.");
        Console.WriteLine($"Geprüfte Datei: {databasePath}");
        connection.Close();
        return;
    }

    Console.WriteLine($"Demo-Bibliothek erkannt: {seriesTotal} Serien, {episodeTotal} Folgen.");
}

// Aufräumen, damit ein zweiter Lauf dasselbe Ergebnis liefert. Ohne das scheitert er an der
// Eindeutigkeit von PlaybackStates.EpisodeId.
using (SqliteCommand reset = connection.CreateCommand())
{
    reset.CommandText = """
        DELETE FROM PlaybackStates;
        DELETE FROM CachedNewReleases;
        UPDATE Series SET IsFavorite = 0, IsWatched = 0;
        """;
    _ = reset.ExecuteNonQuery();
}

// --- 1. Bibliothekspfad ---------------------------------------------------------------
// Über SQL statt über die Oberfläche, weil sich der Ordner-Auswahldialog von Windows nicht
// zuverlässig fernsteuern lässt.
using (SqliteCommand command = connection.CreateCommand())
{
    command.CommandText = "UPDATE AppSettings SET LocalLibraryRootPath = $path, OnlineOnlyMode = 0";
    _ = command.Parameters.AddWithValue("$path", libraryRoot);
    Console.WriteLine($"Bibliothekspfad gesetzt ({command.ExecuteNonQuery()} Zeile(n)): {libraryRoot}");
}

// --- 2. Auszeichnungen ----------------------------------------------------------------
// Eine Handvoll, nicht alle — sonst wirkt die Auszeichnung beliebig.
//
// Achtung: IsSubscribed sagt, ob eine Serie zur Bibliothek gehört, NICHT ob sie überwacht
// wird. Auf 0 gesetzt verschwindet die Serie aus der Mediathek.
string[] favorites = ["Sherlock Holmes", "Die Schatzinsel", "Dracula", "Die Zeitmaschine", "Peter Pan", "Moby Dick"];

foreach (string title in favorites)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "UPDATE Series SET IsFavorite = 1 WHERE Title = $title";
    _ = command.Parameters.AddWithValue("$title", title);
    _ = command.ExecuteNonQuery();
}

// --- 3. Hörstand ----------------------------------------------------------------------
// Einige Serien ganz durch, einige angefangen, der Rest unberührt. Das füllt „Weiterhören"
// und „Zuletzt gehört" auf der Startseite.
(string Series, int Completed, bool InProgress)[] progress =
[
    ("Sherlock Holmes",     4, true),
    ("Die Schatzinsel",     5, false),
    ("Dracula",             2, true),
    ("Alice im Wunderland", 4, false),
    ("Peter Pan",           1, true),
    ("Moby Dick",           3, true),
    ("Tom Sawyer",          4, false),
    ("Robinson Crusoe",     2, true),
    ("Das Dschungelbuch",   3, false),
    ("Die Zeitmaschine",    1, true)
];

int dayOffset = 0;
int stateCount = 0;

foreach ((string seriesTitle, int completed, bool inProgress) in progress)
{
    List<(string Id, long Duration)> episodes = [];
    using (SqliteCommand read = connection.CreateCommand())
    {
        read.CommandText = """
            SELECT e.Id, COALESCE(e.Duration, 0)
            FROM Episodes e JOIN Series s ON s.Id = e.SeriesId
            WHERE s.Title = $title AND e.IsDeleted = 0
            ORDER BY e.EpisodeNumber
            """;
        _ = read.Parameters.AddWithValue("$title", seriesTitle);
        using SqliteDataReader reader = read.ExecuteReader();
        while (reader.Read())
        {
            episodes.Add((reader.GetString(0), reader.GetInt64(1)));
        }
    }

    for (int i = 0; i < episodes.Count; i++)
    {
        bool isCompleted = i < completed;
        bool isStarted = inProgress && i == completed;

        if (!isCompleted && !isStarted)
        {
            continue;
        }

        DateTime playedAt = reference.AddDays(-dayOffset).AddHours(-i);
        dayOffset++;

        // Position: bei abgeschlossenen Folgen am Ende, bei angefangenen mittendrin.
        long duration = episodes[i].Duration > 0 ? episodes[i].Duration : 900;
        long position = isCompleted ? duration : duration / 3;

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PlaybackStates
                (Id, EpisodeId, LastPosition, IsCompleted, CompletedAt, LastPlayedAt, CreatedAt, UpdatedAt, IsDeleted)
            VALUES ($id, $episode, $position, $completed, $completedAt, $playedAt, $playedAt, $playedAt, 0)
            """;
        _ = command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        _ = command.Parameters.AddWithValue("$episode", episodes[i].Id);
        _ = command.Parameters.AddWithValue("$position", position);
        _ = command.Parameters.AddWithValue("$completed", isCompleted ? 1 : 0);
        _ = command.Parameters.AddWithValue("$completedAt", isCompleted ? playedAt.ToString("O", CultureInfo.InvariantCulture) : (object)DBNull.Value);
        _ = command.Parameters.AddWithValue("$playedAt", playedAt.ToString("O", CultureInfo.InvariantCulture));
        _ = command.ExecuteNonQuery();
        stateCount++;
    }
}

// --- 4. Neuerscheinungen --------------------------------------------------------------
// Selbst gesetzt statt beim Anbieter abgefragt: Trägt eine überwachte Demo-Serie einen Namen,
// den es wirklich gibt, holt die Anwendung dessen Cover — und genau das soll nicht ins Bild.
Dictionary<string, string> seriesIds = [];
using (SqliteCommand read = connection.CreateCommand())
{
    read.CommandText = "SELECT Id, Title FROM Series WHERE IsDeleted = 0";
    using SqliteDataReader reader = read.ExecuteReader();
    while (reader.Read())
    {
        seriesIds[reader.GetString(1)] = reader.GetString(0);
    }
}

(string Series, string Episode, int Number, int DaysAgo)[] releases =
[
    ("Sherlock Holmes",  "Der Bund der Rothaarigen", 7, 2),
    ("Die Schatzinsel",  "Die Rückkehr",             6, 9),
    ("Dracula",          "Das Ende der Jagd",        5, 16),
    ("Peter Pan",        "Der Zweikampf",            5, 24),
    ("Moby Dick",        "Der weiße Wal",            5, 38),
    ("Die Zeitmaschine", "Zurück ins Jetzt",         5, 52),
    ("Frankenstein",     "Das Eismeer",              5, 67),
    ("Krieg der Welten", "Die Stille danach",        5, 81)
];

// CollectionId ist eindeutig; feste Werte weit außerhalb echter Kennungen vermeiden
// Zusammenstöße mit Einträgen aus einer früheren Abfrage.
long collectionId = 900001;
int releaseCount = 0;

foreach ((string seriesTitle, string episodeTitle, int number, int daysAgo) in releases)
{
    if (!seriesIds.TryGetValue(seriesTitle, out string? seriesId))
    {
        Console.WriteLine($"  übersprungen (Serie fehlt): {seriesTitle}");
        continue;
    }

    string releaseDate = reference.AddDays(-daysAgo).ToString("O", CultureInfo.InvariantCulture);
    string checkedAt = reference.ToString("O", CultureInfo.InvariantCulture);

    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO CachedNewReleases
                (Id, SeriesId, Title, EpisodeNumber, ReleaseDate, CoverUrl, CollectionId, CheckedAtUtc, CreatedAt, UpdatedAt, IsDeleted)
            VALUES ($id, $series, $title, $number, $release, '', $collection, $checked, $checked, $checked, 0)
            """;
        _ = command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        _ = command.Parameters.AddWithValue("$series", seriesId);
        _ = command.Parameters.AddWithValue("$title", episodeTitle);
        _ = command.Parameters.AddWithValue("$number", number);
        _ = command.Parameters.AddWithValue("$release", releaseDate);
        _ = command.Parameters.AddWithValue("$collection", collectionId++);
        _ = command.Parameters.AddWithValue("$checked", checkedAt);
        _ = command.ExecuteNonQuery();
    }

    using (SqliteCommand watch = connection.CreateCommand())
    {
        watch.CommandText = "UPDATE Series SET IsWatched = 1 WHERE Id = $id";
        _ = watch.Parameters.AddWithValue("$id", seriesId);
        _ = watch.ExecuteNonQuery();
    }

    releaseCount++;
}

// --- Kontrolle ------------------------------------------------------------------------
using (SqliteCommand summary = connection.CreateCommand())
{
    summary.CommandText = """
        SELECT
            (SELECT COUNT(*) FROM Series WHERE IsDeleted = 0),
            (SELECT COUNT(*) FROM Series WHERE IsFavorite = 1 AND IsDeleted = 0),
            (SELECT COUNT(*) FROM Episodes WHERE IsDeleted = 0),
            (SELECT COUNT(*) FROM PlaybackStates WHERE IsCompleted = 1 AND IsDeleted = 0),
            (SELECT COUNT(*) FROM PlaybackStates WHERE IsCompleted = 0 AND IsDeleted = 0)
        """;
    using SqliteDataReader reader = summary.ExecuteReader();
    if (reader.Read())
    {
        Console.WriteLine($"Serien: {reader.GetInt32(0)}, davon Favoriten: {reader.GetInt32(1)}");
        Console.WriteLine($"Folgen: {reader.GetInt32(2)}, gehört: {reader.GetInt32(3)}, angefangen: {reader.GetInt32(4)}");
    }
}

connection.Close();
Console.WriteLine($"Hörstand für {stateCount} Folgen gesetzt, {releaseCount} Neuerscheinungen eingetragen.");
