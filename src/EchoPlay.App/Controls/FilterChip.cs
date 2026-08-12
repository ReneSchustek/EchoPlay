using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using Windows.UI.Text;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Ein Filter der Filterleiste, als Chip zum Umschalten.
    /// <para>
    /// Was gewählt ist, sieht man ohne zu klicken — deshalb Chips und keine Aufklappliste.
    /// Der aktive Zustand hängt an Schriftschnitt und Rahmen, nicht allein an der Farbe.
    /// </para>
    /// </summary>
    public sealed class FilterChip : INotifyPropertyChanged
    {
        private bool _isActive;

        /// <summary>
        /// Erzeugt einen Filter-Chip.
        /// </summary>
        /// <param name="key">Schlüssel, über den die Seite den Filter wiedererkennt.</param>
        /// <param name="label">Beschriftung des Chips.</param>
        /// <param name="isActive">Ob der Filter zu Beginn wirkt.</param>
        public FilterChip(string key, string label, bool isActive = false)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            Key = key;
            Label = label;
            _isActive = isActive;
        }

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Schlüssel des Filters.</summary>
        public string Key { get; }

        /// <summary>Beschriftung des Chips.</summary>
        public string Label { get; }

        /// <summary>Ob der Filter gerade wirkt.</summary>
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                {
                    return;
                }

                _isActive = value;
                Raise(nameof(IsActive));
                Raise(nameof(LabelWeight));
                Raise(nameof(BorderStrength));
            }
        }

        /// <summary>Schriftschnitt: Ein wirkender Filter steht fett.</summary>
        /// <remarks>
        /// Zahlwert statt <c>FontWeights.SemiBold</c> — jene Klasse ist eine WinRT-Komponente
        /// und ohne laufenden XAML-Host nicht ansprechbar.
        /// </remarks>
        public FontWeight LabelWeight => new() { Weight = IsActive ? SemiBoldWeight : NormalWeight };

        /// <summary>Schriftstärke für einen ruhenden Filter.</summary>
        internal const ushort NormalWeight = 400;

        /// <summary>Schriftstärke für einen wirkenden Filter.</summary>
        internal const ushort SemiBoldWeight = 600;

        /// <summary>Rahmenstärke: Ein wirkender Filter trägt einen kräftigeren Rahmen.</summary>
        public Thickness BorderStrength => IsActive ? new Thickness(2) : new Thickness(1);

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
