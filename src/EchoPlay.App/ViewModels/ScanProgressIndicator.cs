using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.UI.Xaml;
using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Der Fortschritt eines laufenden Vorgangs in der unteren Leiste — und derselbe
    /// Fortschritt im Symbol der Taskleiste.
    /// </summary>
    /// <remarks>
    /// Beides gehört zusammen: Jede Stelle, die den Text setzt, muss auch die Taskleiste
    /// nachziehen. Solange das im Ansichtsmodell der Leiste stand, war das eine Absprache;
    /// als eigener Typ ist es eine Zusage.
    /// </remarks>
    public sealed class ScanProgressIndicator : ObservableObject
    {
        private readonly TaskbarProgressService _taskbar;

        private string _text = string.Empty;
        private double _value;
        private bool _isActive;

        /// <summary>
        /// Richtet die Anzeige auf das Taskleisten-Symbol ein.
        /// </summary>
        /// <param name="taskbar">Zeichnet denselben Fortschritt im Symbol der Taskleiste.</param>
        public ScanProgressIndicator(TaskbarProgressService taskbar)
        {
            _taskbar = taskbar;
        }

        /// <summary>
        /// Der Fortschrittstext, etwa „Scanne TKKG …". Leer, wenn nichts läuft.
        /// </summary>
        public string Text
        {
            get => _text;
            private set => SetProperty(ref _text, value);
        }

        /// <summary>
        /// Der Fortschritt in Prozent (0 bis 100). Null heißt: Die Gesamtzahl ist noch
        /// unbekannt, der Balken läuft ohne festen Wert.
        /// </summary>
        public double Value
        {
            get => _value;
            private set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(IsIndeterminate));
                }
            }
        }

        /// <summary>
        /// Ob gerade ein Vorgang läuft. Steuert die Sichtbarkeit der Anzeige.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            private set
            {
                if (SetProperty(ref _isActive, value))
                {
                    OnPropertyChanged(nameof(Visibility));
                    OnPropertyChanged(nameof(IsIndeterminate));
                }
            }
        }

        /// <summary>
        /// Ob der Balken ohne festen Wert laufen soll — ein Vorgang läuft, aber die
        /// Gesamtzahl der Dateien steht noch nicht fest.
        /// </summary>
        public bool IsIndeterminate => _isActive && _value <= 0;

        /// <summary>Sichtbarkeit der Anzeige in der unteren Leiste.</summary>
        public Visibility Visibility =>
            _isActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        /// <summary>
        /// Übernimmt Text und Prozentwert eines laufenden Durchlaufs.
        /// </summary>
        /// <param name="progress">Der gemeldete Stand.</param>
        public void Update(ScanProgress progress)
        {
            ArgumentNullException.ThrowIfNull(progress);

            Text = progress.StatusText;
            Value = progress.PercentComplete;
            IsActive = true;

            // Solange die Gesamtzahl unbekannt ist (0 %), läuft auch die Taskleiste ohne
            // festen Wert.
            if (progress.PercentComplete > 0)
            {
                _taskbar.SetProgress(progress.PercentComplete);
            }
            else
            {
                _taskbar.SetIndeterminate();
            }
        }

        /// <summary>
        /// Zeigt einen Text ohne Prozentwert — für Vorgänge, deren Umfang nicht feststeht.
        /// </summary>
        /// <param name="text">Der anzuzeigende Text.</param>
        public void SetText(string text)
        {
            Text = text;
            Value = 0;
            IsActive = true;
            _taskbar.SetIndeterminate();
        }

        /// <summary>
        /// Beendet die Anzeige und räumt das Taskleisten-Symbol.
        /// </summary>
        public void Clear()
        {
            Text = string.Empty;
            Value = 0;
            IsActive = false;
            _taskbar.Clear();
        }
    }
}
