using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;

namespace EchoPlay.Core.Tests.Replication
{
    /// <summary>
    /// Tests für das Protokollformat des Abgleichs: Textform der Felder, Lesen einer Datei,
    /// Dateinamen und Lesereihenfolge, und die einzelnen Regeln der Zusammenführung.
    /// </summary>
    public sealed class ExchangeFormatTests
    {
        private static readonly Guid DeviceA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid DeviceB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        /// <summary>Eine Aufzählung, an der die Textform von Aufzählungen geprüft wird.</summary>
        private enum Sample
        {
            First,
            Second,
        }

        private static ChangeRecord Record(string kind, long wall, Guid device, params (string Name, string? Value)[] fields) => new(
            kind, "schlüssel", null, new HybridTimestamp(wall, 0, device), false,
            fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal));

        [Theory]
        [InlineData(null, null)]
        [InlineData("Text", "Text")]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        [InlineData(-42, "-42")]
        [InlineData(9_000_000_000L, "9000000000")]
        [InlineData(0.25, "0.25")]
        [InlineData(Sample.Second, "Second")]
        public void Format_UsesTheFixedForms(object? value, string? expected) => Assert.Equal(expected, FieldText.Format(value));

        [Fact]
        public void Format_TimeSpanDateTimeAndGuid()
        {
            Assert.Equal("01:02:03", FieldText.Format(new TimeSpan(1, 2, 3)));
            Assert.Equal("2026-11-20T12:00:00.0000000Z", FieldText.Format(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(new string('a', 32), FieldText.Format(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
            _ = Assert.Throws<NotSupportedException>(() => FieldText.Format(new Uri("https://example.org")));
        }

        [Theory]
        [InlineData("true", typeof(bool), true)]
        [InlineData("-7", typeof(int), -7)]
        [InlineData("7", typeof(int?), 7)]
        [InlineData("9000000000", typeof(long), 9_000_000_000L)]
        [InlineData("0.5", typeof(double), 0.5)]
        [InlineData("First", typeof(Sample), Sample.First)]
        [InlineData("Text", typeof(string), "Text")]
        public void TryParse_ReadsWhatFormatWrites(string text, Type type, object expected)
        {
            Assert.True(FieldText.TryParse(text, type, out object? value));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void TryParse_RoundTripsTimesAndIds()
        {
            DateTime time = new(2026, 11, 20, 12, 30, 15, DateTimeKind.Utc);
            Guid id = Guid.NewGuid();
            Assert.True(FieldText.TryParse(FieldText.Format(time), typeof(DateTime?), out object? parsedTime));
            Assert.True(FieldText.TryParse(FieldText.Format(TimeSpan.FromMinutes(90)), typeof(TimeSpan), out object? parsedSpan));
            Assert.True(FieldText.TryParse(FieldText.Format(id), typeof(Guid), out object? parsedId));

            Assert.Equal(time, parsedTime);
            Assert.Equal(TimeSpan.FromMinutes(90), parsedSpan);
            Assert.Equal(id, parsedId);
        }

        [Theory]
        [InlineData("ja", typeof(bool))]
        [InlineData("1,5", typeof(double))]
        [InlineData("x", typeof(int))]
        [InlineData("x", typeof(long))]
        [InlineData("90 Minuten", typeof(TimeSpan))]
        [InlineData("20.11.2026", typeof(DateTime))]
        [InlineData("keine", typeof(Guid))]
        [InlineData("Third", typeof(Sample))]
        [InlineData("5", typeof(Sample))]
        [InlineData("x", typeof(Uri))]
        public void TryParse_RejectsTextOfTheWrongForm(string text, Type type)
            => Assert.False(FieldText.TryParse(text, type, out _));

        [Fact]
        public void TryParse_NullFitsOnlyNullableTargets()
        {
            Assert.True(FieldText.TryParse(null, typeof(string), out object? text));
            Assert.Null(text);
            Assert.True(FieldText.TryParse(null, typeof(int?), out _));
            Assert.False(FieldText.TryParse(null, typeof(int), out _));
        }

        [Fact]
        public void Batch_RoundTripsThroughJson()
        {
            ChangeRecord record = Record(ChangeKinds.Series, 5, DeviceA, ("Title", "TKKG"), ("Description", null)) with { Parent = "eltern", Deleted = true };
            ChangeBatch batch = new(ChangeBatch.CurrentFormatVersion, DeviceA, 3, true, [record]);

            Assert.True(ChangeBatch.TryParse(batch.ToJson(), out ChangeBatch? parsed, out string? error), error);

            Assert.Equal(DeviceA, parsed!.DeviceId);
            Assert.Equal(3, parsed.Sequence);
            Assert.True(parsed.IsSnapshot);
            Assert.Equal(record, Assert.Single(parsed.Records));
        }

        [Theory]
        [InlineData(null, "leer")]
        [InlineData("{abgeschnitten", "JSON")]
        [InlineData("null", "leer")]
        [InlineData("""{"formatVersion":2}""", "Protokollformat 2")]
        [InlineData("""{"formatVersion":1,"deviceId":"x"}""", "Geräte-Kennung")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":0}""", "Nummer")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1}""", "Sätze")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1,"records":[{"kind":"Cover"}]}""", "Unbekannte Art")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1,"records":[{"kind":"Series","key":""}]}""", "Schlüssel")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1,"records":[{"kind":"Series","key":"k","stamp":"gestern"}]}""", "Zeitpunkt")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1,"records":[{"kind":"Series","key":"k","stamp":"000000000000001.000000000.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}""", "Felder")]
        [InlineData("""{"formatVersion":1,"deviceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sequence":1,"records":[{"kind":"Series","key":"k","stamp":"000000000000001.000000000.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","fields":{"":"x"}}]}""", "Felder")]
        public void Batch_RejectsBrokenFiles(string? json, string expected)
        {
            Assert.False(ChangeBatch.TryParse(json, out ChangeBatch? batch, out string? error));
            Assert.Null(batch);
            Assert.Contains(expected, error, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("000000000007.json", 7, false)]
        [InlineData("000000000012.snapshot.json", 12, true)]
        public void Layout_ReadsProtocolNames(string name, long sequence, bool isSnapshot)
        {
            Assert.True(ExchangeLayout.TryRead(Path.Combine("x", name), out ExchangeFile? file));
            Assert.Equal(sequence, file!.Sequence);
            Assert.Equal(isSnapshot, file.IsSnapshot);
            Assert.Equal(name, ExchangeLayout.FileName(sequence, isSnapshot));
        }

        [Theory]
        [InlineData("000000000007.json.tmp")]
        [InlineData("7.json")]
        [InlineData("00000000000a.json")]
        [InlineData("000000000000.json")]
        [InlineData("notizen.txt")]
        public void Layout_IgnoresEverythingElse(string name) => Assert.False(ExchangeLayout.TryRead(name, out _));

        [Fact]
        public void Layout_DeviceFolderUsesTheId()
            => Assert.Equal(Path.Combine("G", "changes", new string('a', 32)), ExchangeLayout.DeviceFolder("G", DeviceA));

        [Fact]
        public void Pending_StartsWithTheLatestSnapshot_AndStopsAtAGap()
        {
            List<ExchangeFile> files =
            [
                new(1, false, "1"), new(2, false, "2"), new(2, true, "2s"), new(3, false, "3"), new(5, false, "5"),
            ];

            Assert.Equal(["2s", "3"], ExchangeLayout.PendingAfter(files, 0).Select(f => f.Path));
            Assert.Equal(["3"], ExchangeLayout.PendingAfter(files, 2).Select(f => f.Path));
            Assert.Empty(ExchangeLayout.PendingAfter(files, 3));
            Assert.Equal(["5"], ExchangeLayout.PendingAfter(files, 4).Select(f => f.Path));
        }

        [Fact]
        public void Merge_DefaultKind_TheLatestWins_IncludingDeletion()
        {
            ChangeRecord older = Record(ChangeKinds.Series, 1, DeviceA, ("Title", "alt"));
            ChangeRecord newer = Record(ChangeKinds.Series, 2, DeviceB, ("Title", "neu")) with { Deleted = true };

            Assert.Same(newer, ChangeMerge.Merge(older, newer));
            Assert.Same(newer, ChangeMerge.Merge(newer, older));
        }

        [Fact]
        public void Merge_Playback_PositionFromTheLatestPlay_CompletedStays_EarliestCompletion()
        {
            ChangeRecord played = Record(ChangeKinds.PlaybackState, 1, DeviceA,
                (ChangeMerge.LastPositionField, "00:10:00"), (ChangeMerge.LastPlayedAtField, "2026-11-20T12:00:00.0000000Z"),
                (ChangeMerge.IsCompletedField, "true"), (ChangeMerge.CompletedAtField, "2026-11-18T00:00:00.0000000Z"));
            ChangeRecord marked = Record(ChangeKinds.PlaybackState, 9, DeviceB,
                (ChangeMerge.LastPositionField, "00:00:00"), (ChangeMerge.LastPlayedAtField, "2026-11-01T00:00:00.0000000Z"),
                (ChangeMerge.IsCompletedField, "true"), (ChangeMerge.CompletedAtField, "2026-11-19T00:00:00.0000000Z"));

            ChangeRecord merged = ChangeMerge.Merge(played, marked);

            Assert.Equal(marked.Stamp, merged.Stamp);
            Assert.Equal("00:10:00", merged.Field(ChangeMerge.LastPositionField));
            Assert.Equal("true", merged.Field(ChangeMerge.IsCompletedField));
            Assert.Equal("2026-11-18T00:00:00.0000000Z", merged.Field(ChangeMerge.CompletedAtField));
        }

        [Fact]
        public void Merge_Playback_UnmarkedEverywhere_TakesTheLatestCompletionField()
        {
            ChangeRecord a = Record(ChangeKinds.PlaybackState, 1, DeviceA, (ChangeMerge.IsCompletedField, "false"), (ChangeMerge.CompletedAtField, null));
            ChangeRecord b = Record(ChangeKinds.PlaybackState, 2, DeviceB, (ChangeMerge.IsCompletedField, "false"), (ChangeMerge.CompletedAtField, null),
                (ChangeMerge.LastPositionField, "00:01:00"));

            ChangeRecord merged = ChangeMerge.Merge(a, b);

            Assert.Equal("false", merged.Field(ChangeMerge.IsCompletedField));
            Assert.Null(merged.Field(ChangeMerge.CompletedAtField));
            Assert.Equal("00:01:00", merged.Field(ChangeMerge.LastPositionField));
        }

        [Fact]
        public void MergeAll_OrderDoesNotMatter_AndEqualStampsDecideByContent()
        {
            ChangeRecord a = Record(ChangeKinds.Series, 1, DeviceA, ("Title", "a"));
            ChangeRecord b = Record(ChangeKinds.Series, 1, DeviceA, ("Title", "b"));
            ChangeRecord c = Record(ChangeKinds.Series, 0, DeviceB, ("Title", "c"));

            Assert.Equal(ChangeMerge.MergeAll([a, b, c]), ChangeMerge.MergeAll([c, b, a]));
            Assert.Equal("b", ChangeMerge.MergeAll([a, b, c]).Field("Title"));
        }

        [Fact]
        public void Merge_DifferentRecords_IsRefused()
        {
            ChangeRecord series = Record(ChangeKinds.Series, 1, DeviceA);
            _ = Assert.Throws<ArgumentException>(() => ChangeMerge.Merge(series, series with { Key = "anders" }));
            _ = Assert.Throws<ArgumentException>(() => ChangeMerge.Merge(series, series with { Kind = ChangeKinds.Episode }));
        }

        [Fact]
        public void Record_EqualityIgnoresFieldOrder_AndWithFieldsCopies()
        {
            ChangeRecord a = Record(ChangeKinds.Series, 1, DeviceA, ("A", "1"), ("B", "2"));
            ChangeRecord b = Record(ChangeKinds.Series, 1, DeviceA, ("B", "2"), ("A", "1"));
            ChangeRecord changed = a.WithFields(new HybridTimestamp(2, 0, DeviceA), new Dictionary<string, string?> { ["A"] = "3" });

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, changed);
            Assert.Equal("3", changed.Field("A"));
            Assert.Equal("1", a.Field("A"));
            Assert.Null(a.Field("fehlt"));
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void Kinds_KnowTheirNames()
        {
            Assert.True(ChangeKinds.IsKnown(ChangeKinds.DashboardOrder));
            Assert.False(ChangeKinds.IsKnown("Cover"));
            Assert.False(ChangeKinds.IsKnown(null));
            Assert.Equal(ChangeKinds.Series, ChangeKinds.All[0]);
        }
    }
}
