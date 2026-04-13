using System;

namespace EchoPlay.Data.Tests.Helpers
{
    /// <summary>
    /// Deterministische Identifikatoren für Datenbank-Tests. Erzeugt reproduzierbare
    /// GUIDs aus einem numerischen Index, sodass Test-Failures nicht durch zufällige
    /// IDs verschleiert werden.
    /// </summary>
    internal static class TestIds
    {
        public static readonly DateTime ReferenceDate = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Liefert eine deterministische GUID anhand eines numerischen Indexes.
        /// </summary>
        public static Guid Indexed(int index)
        {
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(index).CopyTo(bytes, 0);
            return new Guid(bytes);
        }
    }
}
