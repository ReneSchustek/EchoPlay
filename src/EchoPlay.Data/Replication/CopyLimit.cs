namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Wie viele Byte eine Kopie haben darf: höchstens eine Grenze, oder genau eine Länge, wie sie
    /// ein Paket für einen Eintrag angibt.
    /// </summary>
    internal readonly record struct CopyLimit
    {
        private CopyLimit(long length, bool isExact)
        {
            Length = length;
            IsExact = isExact;
        }

        /// <summary>Die Obergrenze, bei einer genauen Kopie zugleich die geforderte Länge.</summary>
        public long Length { get; }

        /// <summary>Ob genau <see cref="Length"/> Byte kommen müssen.</summary>
        public bool IsExact { get; }

        /// <summary>Ohne Grenze, etwa beim Schreiben eigener Dateien.</summary>
        public static CopyLimit Unlimited => new(long.MaxValue, isExact: false);

        /// <summary>Höchstens so viele Byte; mehr bricht ab.</summary>
        /// <param name="length">Die Obergrenze.</param>
        /// <returns>Die Grenze.</returns>
        public static CopyLimit AtMost(long length) => new(length, isExact: false);

        /// <summary>Genau so viele Byte; mehr oder weniger bricht ab.</summary>
        /// <param name="length">Die geforderte Länge.</param>
        /// <returns>Die Grenze.</returns>
        public static CopyLimit Exactly(long length) => new(length, isExact: true);
    }
}
