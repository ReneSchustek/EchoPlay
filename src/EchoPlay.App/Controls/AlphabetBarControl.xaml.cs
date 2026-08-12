using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Buchstabenleiste über einer nach Namen geordneten Liste. Meldet den gewählten
    /// Buchstaben über <see cref="LetterSelected"/>; das Springen selbst gehört der Seite,
    /// die ihre Liste kennt.
    /// <para>
    /// Die Leiste filtert nichts und weiß nichts über den Bestand außer, welche Buchstaben
    /// belegt sind. Damit bleibt sie an jeder Liste verwendbar, die Titel führt.
    /// </para>
    /// </summary>
    public sealed partial class AlphabetBarControl : UserControl
    {
        /// <summary>
        /// Initialisiert die Leiste.
        /// </summary>
        public AlphabetBarControl()
        {
            InitializeComponent();
            Rebuild();
        }

        /// <summary>
        /// Wird ausgelöst, wenn ein belegter Buchstabe gewählt wurde.
        /// </summary>
        public event EventHandler<LetterSelectedEventArgs>? LetterSelected;

        /// <summary>
        /// Buchstaben, unter denen der Bestand tatsächlich Einträge hat. Alle übrigen
        /// erscheinen deaktiviert.
        /// </summary>
        public static readonly DependencyProperty OccupiedLettersProperty =
            DependencyProperty.Register(nameof(OccupiedLetters), typeof(IReadOnlyCollection<char>), typeof(AlphabetBarControl),
                new PropertyMetadata(null, (d, e) => ((AlphabetBarControl)d).Rebuild()));

        /// <summary>Belegte Buchstaben.</summary>
        public IReadOnlyCollection<char>? OccupiedLetters
        {
            get => (IReadOnlyCollection<char>?)GetValue(OccupiedLettersProperty);
            set => SetValue(OccupiedLettersProperty, value);
        }

        /// <summary>Zuletzt angesprungener Buchstabe; wird hervorgehoben.</summary>
        public static readonly DependencyProperty CurrentLetterProperty =
            DependencyProperty.Register(nameof(CurrentLetter), typeof(char), typeof(AlphabetBarControl),
                new PropertyMetadata('\0', (d, e) => ((AlphabetBarControl)d).Rebuild()));

        /// <summary>Zuletzt angesprungener Buchstabe.</summary>
        public char CurrentLetter
        {
            get => (char)GetValue(CurrentLetterProperty);
            set => SetValue(CurrentLetterProperty, value);
        }

        /// <summary>
        /// Vorlage für die Beschriftung der Sprachausgabe, mit <c>{0}</c> als Platzhalter für
        /// den Buchstaben — etwa „Zu {0} springen". Ohne Vorlage liest die Sprachausgabe nur
        /// den nackten Buchstaben vor.
        /// </summary>
        public static readonly DependencyProperty AutomationNameFormatProperty =
            DependencyProperty.Register(nameof(AutomationNameFormat), typeof(string), typeof(AlphabetBarControl),
                new PropertyMetadata("{0}", (d, e) => ((AlphabetBarControl)d).Rebuild()));

        /// <summary>Vorlage für die Beschriftung der Sprachausgabe.</summary>
        public string AutomationNameFormat
        {
            get => (string)GetValue(AutomationNameFormatProperty);
            set => SetValue(AutomationNameFormatProperty, value);
        }

        /// <summary>
        /// Baut die Leiste neu auf. Sie führt immer alle Fächer — belegt oder nicht.
        /// </summary>
        private void Rebuild()
        {
            IReadOnlyCollection<char> occupied = OccupiedLetters ?? [];
            char current = CurrentLetter;
            string format = AutomationNameFormat;

            LetterItems.ItemsSource = AlphabetIndex.Buckets
                .Select(letter => new AlphabetBucket(
                    letter,
                    occupied.Contains(letter),
                    letter == current,
                    FormatAutomationName(format, letter)))
                .ToList();
        }

        private static string FormatAutomationName(string format, char letter)
        {
            // Eine Vorlage ohne Platzhalter würde für alle 27 Fächer denselben Namen liefern —
            // die Sprachausgabe könnte die Schaltflächen dann nicht auseinanderhalten.
            return format.Contains("{0}", StringComparison.Ordinal)
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, format, letter)
                : letter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void OnLetterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: char letter })
            {
                LetterSelected?.Invoke(this, new LetterSelectedEventArgs(letter));
            }
        }
    }
}
