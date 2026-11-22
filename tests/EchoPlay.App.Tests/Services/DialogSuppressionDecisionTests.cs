using EchoPlay.App.Services;
using EchoPlay.Core.Models;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Die Regel hinter dem Häkchen. Das Zeigen des Dialogs braucht ein Fenster und ist
    /// deshalb nicht prüfbar, diese Entscheidung schon, und sie ist die, bei der ein
    /// Fehler weh tut: Ein gemerktes „Abbrechen" würde eine Schaltfläche stumm
    /// wirkungslos machen.
    /// </summary>
    public sealed class DialogSuppressionDecisionTests
    {
        [Fact]
        public void Checkbox_WithConsent_IsRemembered()
        {
            Assert.True(DialogSuppressionDecision.ShouldRemember(
                DialogKey.SeriesRemoveFromLibrary, isCheckBoxChecked: true, isAffirmative: true));
        }

        [Fact]
        public void Checkbox_WithDecline_IsNotRemembered()
        {
            Assert.False(DialogSuppressionDecision.ShouldRemember(
                DialogKey.SeriesRemoveFromLibrary, isCheckBoxChecked: true, isAffirmative: false));
        }

        [Fact]
        public void WithoutCheckbox_NothingIsRemembered()
        {
            Assert.False(DialogSuppressionDecision.ShouldRemember(
                DialogKey.SeriesRemoveFromLibrary, isCheckBoxChecked: false, isAffirmative: true));
        }

        [Fact]
        public void WithoutKey_NothingIsRemembered()
        {
            // DialogKey.None steht für die Dialoge, die sich nicht ausblenden lassen.
            Assert.False(DialogSuppressionDecision.ShouldRemember(
                DialogKey.None, isCheckBoxChecked: true, isAffirmative: true));
        }
    }
}
