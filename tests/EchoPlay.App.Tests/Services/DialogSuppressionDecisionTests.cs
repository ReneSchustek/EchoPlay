using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Die Regel hinter dem Häkchen. Das Zeigen des Dialogs braucht ein Fenster und ist
    /// deshalb nicht prüfbar — diese Entscheidung schon, und sie ist die, bei der ein
    /// Fehler weh tut: Ein gemerktes „Abbrechen" würde eine Schaltfläche stumm
    /// wirkungslos machen.
    /// </summary>
    public sealed class DialogSuppressionDecisionTests
    {
        [Fact]
        public void Haekchen_MitZustimmung_WirdGemerkt()
        {
            Assert.True(DialogSuppressionDecision.ShouldRemember(
                DialogKey.SeriesRemoveFromLibrary, isCheckBoxChecked: true, isAffirmative: true));
        }

        [Fact]
        public void Haekchen_MitAblehnung_WirdNichtGemerkt()
        {
            Assert.False(DialogSuppressionDecision.ShouldRemember(
                DialogKey.SeriesRemoveFromLibrary, isCheckBoxChecked: true, isAffirmative: false));
        }

        [Fact]
        public void OhneHaekchen_WirdNichtsGemerkt()
        {
            Assert.False(DialogSuppressionDecision.ShouldRemember(
                DialogKey.SeriesRemoveFromLibrary, isCheckBoxChecked: false, isAffirmative: true));
        }

        [Fact]
        public void OhneKennung_WirdNichtsGemerkt()
        {
            // DialogKey.None steht für die Dialoge, die sich nicht ausblenden lassen.
            Assert.False(DialogSuppressionDecision.ShouldRemember(
                DialogKey.None, isCheckBoxChecked: true, isAffirmative: true));
        }
    }
}
