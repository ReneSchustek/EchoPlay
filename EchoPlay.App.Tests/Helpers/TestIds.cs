using System;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Feste GUIDs für deterministische Test-Fixtures. Ersetzt <c>Guid.NewGuid()</c>
    /// in Setup-Code, damit Test-Failures reproduzierbar sind und nicht mit
    /// <c>Expected &lt;random-guid&gt; to equal &lt;random-guid&gt;</c> enden.
    ///
    /// Namens-Konvention: Die Präfixe entsprechen dem Entity-Typ (Series, Episode,
    /// Track, Settings, Position, Release). Die Ziffern sind eine einfache Zählung
    /// und stehen für keine Reihenfolge im Datenmodell.
    /// </summary>
    internal static class TestIds
    {
        public static readonly Guid SeriesA = new("11111111-1111-1111-1111-111111111111");
        public static readonly Guid SeriesB = new("11111111-1111-1111-1111-111111111112");
        public static readonly Guid SeriesC = new("11111111-1111-1111-1111-111111111113");
        public static readonly Guid SeriesD = new("11111111-1111-1111-1111-111111111114");
        public static readonly Guid SeriesE = new("11111111-1111-1111-1111-111111111115");

        public static readonly Guid EpisodeA = new("22222222-2222-2222-2222-222222222221");
        public static readonly Guid EpisodeB = new("22222222-2222-2222-2222-222222222222");
        public static readonly Guid EpisodeC = new("22222222-2222-2222-2222-222222222223");
        public static readonly Guid EpisodeD = new("22222222-2222-2222-2222-222222222224");
        public static readonly Guid EpisodeE = new("22222222-2222-2222-2222-222222222225");

        public static readonly Guid TrackA = new("33333333-3333-3333-3333-333333333331");
        public static readonly Guid TrackB = new("33333333-3333-3333-3333-333333333332");
        public static readonly Guid TrackC = new("33333333-3333-3333-3333-333333333333");

        public static readonly Guid SettingsA = new("44444444-4444-4444-4444-444444444441");

        public static readonly Guid PositionA = new("55555555-5555-5555-5555-555555555551");
        public static readonly Guid PositionB = new("55555555-5555-5555-5555-555555555552");

        public static readonly Guid ReleaseA = new("66666666-6666-6666-6666-666666666661");
        public static readonly Guid ReleaseB = new("66666666-6666-6666-6666-666666666662");

        /// <summary>
        /// Festes Referenzdatum, identisch mit dem Default von <c>FakeClock</c>.
        /// Alle zeitabhängigen Test-Fixtures verwenden Offsets relativ zu diesem Datum.
        /// </summary>
        public static readonly DateTime ReferenceDate = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Stellt eine deterministische GUID anhand eines numerischen Indexes bereit,
        /// nützlich, wenn Tests dynamisch eine Kollektion erzeugen.
        /// </summary>
        public static Guid Indexed(int index)
        {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(index).CopyTo(bytes, 0);
            return new Guid(bytes);
        }
    }
}
