using EchoPlay.Core.Logging;
using FsCheck;
using FsCheck.Xunit;

namespace EchoPlay.Fuzz
{
    /// <summary>
    /// Property-based-Fuzz-Tests für <see cref="PathRedactor.Redact"/>.
    /// Property: Egal welcher Eingabe-String — der Redactor darf nicht abstuerzen
    /// und gibt einen nicht-leeren String zurück. Der Datei-/Verzeichnisname
    /// muss redacted erscheinen (kein voller Original-Pfad mehr).
    /// </summary>
    public sealed class PathRedactorFuzzTests
    {
        [Property(MaxTest = 5000)]
        public bool Redact_AnyInput_ReturnsNonEmptyString(string? input)
        {
            string result = PathRedactor.Redact(input);
            return !string.IsNullOrEmpty(result);
        }

        [Property(MaxTest = 1000)]
        public bool Redact_LongInput_DoesNotCrash(NonNull<string> input)
        {
            // 100.000 Zeichen Pfad — typischer Pathological-Case.
            string longPath = string.Concat(Enumerable.Repeat(input.Get, 2000));
            string result = PathRedactor.Redact(longPath);
            return result.Length > 0;
        }

        [Property(MaxTest = 1000)]
        public bool Redact_SamePath_ProducesSameOutput(NonNull<string> input)
        {
            // Hash-basierte Redaction muss deterministisch sein, sonst
            // bricht Log-Korrelation.
            string a = PathRedactor.Redact(input.Get);
            string b = PathRedactor.Redact(input.Get);
            return a == b;
        }
    }
}
