using EchoPlay.Core.Replication;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace EchoPlay.Fuzz
{
    /// <summary>
    /// Eigenschaftstests für <see cref="LibraryPathMapper"/>. Ein relativer Pfad kommt beim Einspielen
    /// und im Abgleich von außen; was er auch enthält, er darf nicht aus dem Stamm herausführen.
    /// </summary>
    public sealed class LibraryPathMapperFuzzTests
    {
        private const string Root = @"D:\Mp3\Hörspiele";

        // Bausteine, aus denen fremde Pfade zusammengesetzt werden, darunter die gefährlichen.
        private static readonly string[] Segments =
        [
            "TKKG", "..", ".", "Die drei ???", "Fünf Freunde", "a b", "C:", @"\\nas", "/", @"\", "x.mp3", "%2e%2e", "~", "",
        ];

        [Property(MaxTest = 5000)]
        public bool ToAbsolute_AnyInput_StaysInsideRootOrIsRejected(string? portable)
        {
            string? absolute = LibraryPathMapper.ToAbsolute(Root, portable ?? string.Empty);
            return absolute is null || IsInsideRoot(absolute);
        }

        [Property(MaxTest = 5000)]
        public Property ToAbsolute_ComposedSegments_StaysInsideRootOrIsRejected()
        {
            Gen<string> paths = Gen.Elements(Segments).ListOf().Select(parts => string.Join("/", parts));
            return Prop.ForAll(paths.ToArbitrary(), portable =>
            {
                string? absolute = LibraryPathMapper.ToAbsolute(Root, portable);
                return absolute is null || IsInsideRoot(absolute);
            });
        }

        [Property(MaxTest = 2000)]
        public Property ToPortable_ThenToAbsolute_GivesTheSamePath()
        {
            Gen<string> names = Gen.Elements("TKKG", "Fünf Freunde", "001 - Die Jagd", "Ärger & Co.", "01.mp3", "a b");
            Gen<string> paths = names.NonEmptyListOf().Select(parts => Path.Combine([Root, .. parts]));
            return Prop.ForAll(paths.ToArbitrary(), absolute =>
            {
                string? portable = LibraryPathMapper.ToPortable(Root, absolute);
                return portable is not null && LibraryPathMapper.ToAbsolute(Root, portable) == Path.GetFullPath(absolute);
            });
        }

        private static bool IsInsideRoot(string absolute) =>
            absolute.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
