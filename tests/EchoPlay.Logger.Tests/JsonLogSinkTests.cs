using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="JsonLogSink"/>. Prüft:
    /// 1. Roundtrip: Serialisierter Eintrag ist valides JSON, alle Felder erhalten.
    /// 2. Scope-Flatten: <see cref="LogEntry.Scopes"/> landet als JSON-Array.
    /// 3. Exception-Serialisierung: Typ, Message, StackTrace werden erfasst.
    /// 4. Datei-Ausgabe: WriteAsync erzeugt eine .jsonl-Datei mit einer Zeile pro Eintrag.
    /// </summary>
    /// <remarks>
    /// <c>Guid.NewGuid()</c> als Verzeichnis-Suffix ist bewusst: xUnit fuehrt Tests in
    /// derselben Klasse parallel aus. Filesystem-basierte Sinks brauchen physische
    /// Isolation pro Lauf, sonst greifen Datei-Locks ineinander. Determinismus ist hier
    /// auf Verzeichnis-Eindeutigkeit beschränkt — Test-Ausgaben sind unabhängig vom Suffix.
    /// </remarks>
    public sealed class JsonLogSinkTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(), $"EchoPlayJsonLogSinkTests_{Guid.NewGuid():N}");

        public JsonLogSinkTests()
        {
            _ = Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void Serialize_Roundtrip_PreservesAllFields()
        {
            DateTime ts = new(2026, 4, 17, 15, 30, 45, DateTimeKind.Utc);
            LogEntry entry = new(
                Timestamp: ts,
                Level: LogLevel.Warning,
                Message: "Test mit Umlaut ä und Escape \"char\"",
                Category: "TestCategory",
                Scopes: new List<string> { "OuterScope", "InnerScope" });

            string json = JsonLogSink.Serialize(entry);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            Assert.Equal("TestCategory", root.GetProperty("Category").GetString());
            Assert.Equal("Warning", root.GetProperty("Level").GetString());
            Assert.Equal("Test mit Umlaut ä und Escape \"char\"", root.GetProperty("Message").GetString());
            Assert.Contains("2026-04-17", root.GetProperty("Timestamp").GetString()!, StringComparison.Ordinal);
        }

        [Fact]
        public void Serialize_Scopes_LandAsJsonArray()
        {
            LogEntry entry = new(
                Timestamp: DateTime.UtcNow,
                Level: LogLevel.Information,
                Message: "scoped",
                Category: "Test",
                Scopes: new List<string> { "Outer", "Middle", "Inner" });

            string json = JsonLogSink.Serialize(entry);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement scopes = doc.RootElement.GetProperty("Scopes");

            Assert.Equal(JsonValueKind.Array, scopes.ValueKind);
            Assert.Equal(3, scopes.GetArrayLength());
            Assert.Equal("Outer", scopes[0].GetString());
            Assert.Equal("Middle", scopes[1].GetString());
            Assert.Equal("Inner", scopes[2].GetString());
        }

        [Fact]
        public void Serialize_WithException_IncludesTypeMessageAndStackTrace()
        {
            InvalidOperationException exception;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            LogEntry entry = new(
                Timestamp: DateTime.UtcNow,
                Level: LogLevel.Error,
                Message: "failure",
                Category: "Test",
                Scopes: new List<string>(),
                Exception: exception);

            string json = JsonLogSink.Serialize(entry);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement exceptionNode = doc.RootElement.GetProperty("Exception");

            Assert.Equal("System.InvalidOperationException", exceptionNode.GetProperty("Type").GetString());
            Assert.Equal("boom", exceptionNode.GetProperty("Message").GetString());
            Assert.NotNull(exceptionNode.GetProperty("StackTrace").GetString());
        }

        [Fact]
        public void Serialize_WithoutException_ExceptionFieldIsNull()
        {
            LogEntry entry = new(
                Timestamp: DateTime.UtcNow,
                Level: LogLevel.Information,
                Message: "ok",
                Category: "Test",
                Scopes: new List<string>());

            string json = JsonLogSink.Serialize(entry);
            using JsonDocument doc = JsonDocument.Parse(json);

            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Exception").ValueKind);
        }

        [Fact]
        public async Task WriteAsync_EmitsOneJsonLinePerEntry()
        {
            using JsonLogSink sink = new(_tempDirectory);
            LogEntry first = new(
                Timestamp: DateTime.UtcNow,
                Level: LogLevel.Information,
                Message: "first",
                Category: "Test",
                Scopes: new List<string>());
            LogEntry second = new(
                Timestamp: DateTime.UtcNow,
                Level: LogLevel.Warning,
                Message: "second",
                Category: "Test",
                Scopes: new List<string>());

            await sink.WriteAsync(first);
            await sink.WriteAsync(second);

            string[] files = Directory.GetFiles(_tempDirectory, "*.jsonl");
            _ = Assert.Single(files);
            string[] lines = await File.ReadAllLinesAsync(files[0], cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, lines.Length);
            using JsonDocument doc0 = JsonDocument.Parse(lines[0]);
            using JsonDocument doc1 = JsonDocument.Parse(lines[1]);
            Assert.Equal("first", doc0.RootElement.GetProperty("Message").GetString());
            Assert.Equal("second", doc1.RootElement.GetProperty("Message").GetString());
        }

        [Fact]
        public void Serialize_NullEntry_ThrowsArgumentNullException()
        {
            _ = Assert.Throws<ArgumentNullException>(() => JsonLogSink.Serialize(null!));
        }
    }
}
