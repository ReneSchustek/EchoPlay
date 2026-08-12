using Microsoft.UI.Xaml;
using System.Globalization;
using Windows.UI.Text;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Ein Buchstabe der Sprungleiste samt seiner Darstellung.
    /// <para>
    /// Trägt bewusst keine Farbe: Ob ein Buchstabe belegt und ob er der aktive ist, zeigt
    /// die Leiste über Schriftschnitt und Rahmen. Ein Zustand, der allein an der Farbe
    /// hängt, ist für einen Teil der Nutzer gar kein Zustand — und eine Farbe im Quelltext
    /// wäre zugleich an der Palette vorbei.
    /// </para>
    /// </summary>
    public sealed class AlphabetBucket
    {
        /// <summary>
        /// Erzeugt einen Eintrag der Leiste.
        /// </summary>
        /// <param name="letter">Buchstabe des Fachs, <c>A</c>–<c>Z</c> oder <c>#</c>.</param>
        /// <param name="isOccupied">Ob im Bestand mindestens ein Titel darunter steht.</param>
        /// <param name="isCurrent">Ob dieses Fach zuletzt angesprungen wurde.</param>
        /// <param name="automationName">Beschriftung für die Sprachausgabe.</param>
        public AlphabetBucket(char letter, bool isOccupied, bool isCurrent, string automationName)
        {
            Letter = letter;
            IsOccupied = isOccupied;
            IsCurrent = isCurrent;
            AutomationName = automationName;
        }

        /// <summary>Buchstabe des Fachs.</summary>
        public char Letter { get; }

        /// <summary>Beschriftung auf der Schaltfläche.</summary>
        public string Label => Letter.ToString(CultureInfo.InvariantCulture);

        /// <summary>Ob im Bestand mindestens ein Titel unter diesem Buchstaben steht.</summary>
        public bool IsOccupied { get; }

        /// <summary>Ob dieses Fach zuletzt angesprungen wurde.</summary>
        public bool IsCurrent { get; }

        /// <summary>Beschriftung für die Sprachausgabe, etwa „Zu B springen".</summary>
        public string AutomationName { get; }

        /// <summary>Schriftschnitt: Der aktive Buchstabe steht fett.</summary>
        /// <remarks>
        /// Der Zahlwert steht hier absichtlich statt <c>FontWeights.Bold</c>: Jene Klasse ist
        /// eine WinRT-Komponente und lässt sich ohne laufenden XAML-Host nicht ansprechen —
        /// die Darstellungsregel wäre damit nur in der fertigen Anwendung prüfbar.
        /// </remarks>
        public FontWeight LabelWeight => new() { Weight = IsCurrent ? BoldWeight : NormalWeight };

        /// <summary>Schriftstärke für gewöhnliche Buchstaben.</summary>
        internal const ushort NormalWeight = 400;

        /// <summary>Schriftstärke für den aktiven Buchstaben.</summary>
        internal const ushort BoldWeight = 700;

        /// <summary>Rahmenstärke: Nur der aktive Buchstabe trägt einen Rahmen.</summary>
        public Thickness BorderStrength => IsCurrent ? new Thickness(1) : new Thickness(0);
    }
}
