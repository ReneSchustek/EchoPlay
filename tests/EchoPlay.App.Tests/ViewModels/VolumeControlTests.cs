using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Lautstärke-Bedienung: Regler, Stummschaltung und das Merken über den
    /// Neustart hinaus.
    /// </summary>
    /// <remarks>
    /// Die Klasse hält keinen eigenen Zustand — sie bedient den Wiedergabedienst. Geprüft
    /// wird deshalb, was beim Dienst ankommt und was die Oberfläche als geändert gemeldet
    /// bekommt. Der zweite Teil ist nicht nebensächlich: Ohne Meldung bleibt das Zeichen des
    /// Lautsprechers auf dem alten Stand, obwohl der Ton längst anders ist.
    /// </remarks>
    public sealed class VolumeControlTests
    {
        [Fact]
        public void VolumePercent_ReadsFromThePlayer()
        {
            FakePlayerService player = new() { Volume = 0.42 };
            VolumeControl control = BuildControl(player);

            Assert.Equal(42, control.VolumePercent, precision: 6);
        }

        [Fact]
        public void VolumePercent_WhenSet_PassesTheValueToThePlayer()
        {
            FakePlayerService player = new() { Volume = 0.2 };
            VolumeControl control = BuildControl(player);

            control.VolumePercent = 75;

            Assert.Equal(0.75, player.Volume, precision: 6);
        }

        [Theory]
        [InlineData(150, 1.0)]
        [InlineData(-20, 0.0)]
        public void VolumePercent_OutsideTheScale_IsClamped(double input, double expected)
        {
            FakePlayerService player = new() { Volume = 0.5 };
            VolumeControl control = BuildControl(player);

            control.VolumePercent = input;

            // Der Regler liefert je nach Eingabegerät auch Werte außerhalb seiner Skala.
            // Ein Lautstärkewert über 1.0 ist für das Abspielgerät keine gültige Angabe.
            Assert.Equal(expected, player.Volume, precision: 6);
        }

        [Fact]
        public void VolumePercent_TurnedUpWhileMuted_EndsTheMute()
        {
            FakePlayerService player = new() { Volume = 0, IsMuted = true };
            VolumeControl control = BuildControl(player);

            control.VolumePercent = 30;

            // Wer am Regler dreht, will hören. Bliebe die Stummschaltung bestehen, drehte
            // der Anwender ins Leere und suchte den Fehler beim Abspielgerät.
            Assert.False(player.IsMuted);
        }

        [Fact]
        public void VolumePercent_SetToTheSameValue_ReportsNothing()
        {
            FakePlayerService player = new() { Volume = 0.5 };
            VolumeControl control = BuildControl(player);
            List<string> reported = RecordPropertyChanges(control);

            control.VolumePercent = 50;

            // Ein Regler feuert beim Ziehen dutzende Male, oft mit demselben Wert. Jede
            // dieser Wiederholungen zu melden, hielte die Oberfläche unnötig in Bewegung.
            Assert.Empty(reported);
        }

        [Fact]
        public void VolumePercent_WhenChanged_ReportsAllDerivedValues()
        {
            FakePlayerService player = new() { Volume = 0.5 };
            VolumeControl control = BuildControl(player);
            List<string> reported = RecordPropertyChanges(control);

            control.VolumePercent = 80;

            Assert.Contains(nameof(VolumeControl.VolumePercent), reported);
            Assert.Contains(nameof(VolumeControl.IsMuted), reported);
            Assert.Contains(nameof(VolumeControl.VolumeGlyph), reported);
        }

        [Fact]
        public void VolumeGlyph_WhenMuted_MatchesSilenceRegardlessOfLevel()
        {
            // Absichtlich ohne feste Zeichen: Welches Symbol die Schrift liefert, ist eine
            // Gestaltungsfrage und darf sich ändern. Die Zusage ist, dass Stummschaltung
            // aussieht wie Stille — auch bei aufgedrehtem Regler.
            string silent = BuildControl(new FakePlayerService { Volume = 0, IsMuted = false }).VolumeGlyph;
            string mutedButLoud = BuildControl(new FakePlayerService { Volume = 0.9, IsMuted = true }).VolumeGlyph;

            Assert.Equal(silent, mutedButLoud);
        }

        [Fact]
        public void VolumeGlyph_AcrossLevels_IsDistinguishable()
        {
            string silent = BuildControl(new FakePlayerService { Volume = 0 }).VolumeGlyph;
            string low = BuildControl(new FakePlayerService { Volume = 0.2 }).VolumeGlyph;
            string medium = BuildControl(new FakePlayerService { Volume = 0.5 }).VolumeGlyph;
            string high = BuildControl(new FakePlayerService { Volume = 0.9 }).VolumeGlyph;

            // Vier Stufen, vier Zeichen: Fallen zwei zusammen, sieht der Anwender an der
            // Anzeige nicht mehr, wo er steht.
            Assert.Equal(4, new HashSet<string> { silent, low, medium, high }.Count);
        }

        [Fact]
        public async Task ToggleMute_FromAudible_MutesAndRemembersIt()
        {
            FakePlayerService player = new() { Volume = 0.6, IsMuted = false };
            FakeAppSettingsDataService settingsService = new();
            VolumeControl control = BuildControl(player, settingsService);

            control.ToggleMuteCommand.Execute(null);
            await WaitForSaveAsync(settingsService);

            Assert.True(player.IsMuted);
            AppSettings saved = await settingsService.GetAsync(TestContext.Current.CancellationToken);
            Assert.True(saved.IsMuted);
            Assert.Equal(0.6, saved.Volume, precision: 6);
        }

        [Fact]
        public async Task ToggleMute_WhenVolumeIsZero_GivesPlaybackSomethingAudible()
        {
            FakePlayerService player = new() { Volume = 0, IsMuted = true };
            FakeAppSettingsDataService settingsService = new();
            VolumeControl control = BuildControl(player, settingsService);

            control.ToggleMuteCommand.Execute(null);
            await WaitForSaveAsync(settingsService);

            // Stummschaltung aufheben bei ganz heruntergedrehtem Regler ergäbe wieder
            // Stille — der Anwender hielte die Schaltfläche für kaputt.
            Assert.False(player.IsMuted);
            Assert.True(player.Volume > 0);
        }

        [Fact]
        public async Task Commit_WritesVolumeAndMuteIntoTheSettings()
        {
            FakePlayerService player = new() { Volume = 0.33, IsMuted = true };
            FakeAppSettingsDataService settingsService = new();
            VolumeControl control = BuildControl(player, settingsService);

            await control.CommitAsync();

            AppSettings saved = await settingsService.GetAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0.33, saved.Volume, precision: 6);
            Assert.True(saved.IsMuted);
            Assert.Equal(1, settingsService.SaveCallCount);
        }

        [Fact]
        public void Refresh_ReportsTheDerivedValues()
        {
            FakePlayerService player = new() { Volume = 0.5 };
            VolumeControl control = BuildControl(player);
            List<string> reported = RecordPropertyChanges(control);

            // Wird die Lautstärke von außen gesetzt — beim Start aus den Einstellungen —,
            // erfährt die Oberfläche davon nur über diesen Weg.
            control.Refresh();

            Assert.Contains(nameof(VolumeControl.VolumePercent), reported);
            Assert.Contains(nameof(VolumeControl.VolumeGlyph), reported);
        }

        private static List<string> RecordPropertyChanges(VolumeControl control)
        {
            List<string> reported = [];
            control.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null)
                {
                    reported.Add(e.PropertyName);
                }
            };
            return reported;
        }

        /// <summary>
        /// Der Stummschalt-Befehl speichert nebenher. Der Befehl selbst liefert keinen Task,
        /// deshalb wird auf den Schreibvorgang gewartet, statt eine Wartezeit zu raten.
        /// </summary>
        private static async Task WaitForSaveAsync(FakeAppSettingsDataService settingsService)
        {
            for (int attempt = 0; attempt < 100 && settingsService.SaveCallCount == 0; attempt++)
            {
                await Task.Yield();
            }
        }

        private static VolumeControl BuildControl(
            FakePlayerService player,
            FakeAppSettingsDataService? settingsService = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService ?? new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new VolumeControl(player, provider.GetRequiredService<IServiceScopeFactory>());
        }
    }
}
