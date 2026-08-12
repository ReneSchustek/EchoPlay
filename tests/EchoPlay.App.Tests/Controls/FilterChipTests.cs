using EchoPlay.App.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Xunit;

namespace EchoPlay.App.Tests.Controls
{
    /// <summary>
    /// Sichert ab, dass ein wirkender Filter sichtbar ist. Ein Filter, der wirkt, aber nicht
    /// zu sehen ist, erzeugt den Eindruck fehlender Daten — und man sucht den Fehler in den
    /// Daten statt in der Leiste.
    /// </summary>
    public sealed class FilterChipTests
    {
        [Fact]
        public void ActiveChip_IsEmphasised()
        {
            FilterChip chip = new("unplayed", "Ungehört", isActive: true);

            Assert.Equal(FilterChip.SemiBoldWeight, chip.LabelWeight.Weight);
            Assert.Equal(2, chip.BorderStrength.Left);
        }

        [Fact]
        public void InactiveChip_KeepsAThinBorder()
        {
            FilterChip chip = new("unplayed", "Ungehört");

            Assert.False(chip.IsActive);
            Assert.Equal(FilterChip.NormalWeight, chip.LabelWeight.Weight);
            Assert.Equal(1, chip.BorderStrength.Left);
        }

        /// <summary>
        /// Ohne Meldung der abgeleiteten Eigenschaften bliebe die Hervorhebung beim Umschalten
        /// stehen: Der Filter wirkte, sähe aber unverändert aus.
        /// </summary>
        [Fact]
        public void TogglingActive_NotifiesDerivedProperties()
        {
            FilterChip chip = new("unplayed", "Ungehört");
            List<string?> changed = [];
            ((INotifyPropertyChanged)chip).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            chip.IsActive = true;

            Assert.Contains(nameof(FilterChip.IsActive), changed);
            Assert.Contains(nameof(FilterChip.LabelWeight), changed);
            Assert.Contains(nameof(FilterChip.BorderStrength), changed);
        }

        [Fact]
        public void SettingSameValue_DoesNotNotify()
        {
            FilterChip chip = new("unplayed", "Ungehört", isActive: true);
            List<string?> changed = [];
            ((INotifyPropertyChanged)chip).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            chip.IsActive = true;

            Assert.Empty(changed);
        }

        [Fact]
        public void EmptyKey_IsRejected() =>
            Assert.Throws<ArgumentException>(() => new FilterChip(string.Empty, "Ungehört"));
    }
}
