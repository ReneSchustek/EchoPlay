using EchoPlay.App.Services;
using System;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SleepTimer"/>.
    /// </summary>
    /// <remarks>
    /// Solange das Herunterzählen im Wiedergabedienst stand, war es nur mit laufender
    /// Wiedergabe und echtem Warten prüfbar. Als eigener Typ genügt ein Aufruf je Takt.
    /// </remarks>
    public sealed class SleepTimerTests
    {
        [Fact]
        public void Tick_OhneEingestellteDauer_MeldetNichts()
        {
            SleepTimer timer = new();

            Assert.False(timer.Tick(TimeSpan.FromSeconds(1)));
            Assert.Null(timer.Remaining);
        }

        [Fact]
        public void Tick_ZiehtDieVerstricheneZeitAb()
        {
            SleepTimer timer = new();
            timer.Set(TimeSpan.FromSeconds(10));

            Assert.False(timer.Tick(TimeSpan.FromSeconds(4)));

            Assert.Equal(TimeSpan.FromSeconds(6), timer.Remaining);
        }

        [Fact]
        public void Tick_BeiAblauf_MeldetEinmalUndSchaltetAb()
        {
            // Die Meldung darf genau einmal kommen: Der Aufrufer hält daraufhin die Wiedergabe
            // an — ein zweites Mal würde er sie erneut anhalten, obwohl der Nutzer sie
            // inzwischen vielleicht wieder gestartet hat.
            SleepTimer timer = new();
            timer.Set(TimeSpan.FromSeconds(1));

            Assert.True(timer.Tick(TimeSpan.FromSeconds(1)));
            Assert.Null(timer.Remaining);

            Assert.False(timer.Tick(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void Tick_UeberschreitetDieDauer_MeldetTrotzdem()
        {
            // Der Takt kann gröber sein als die Restzeit — dann liegt der Wert unter null und
            // muss trotzdem als abgelaufen gelten.
            SleepTimer timer = new();
            timer.Set(TimeSpan.FromMilliseconds(200));

            Assert.True(timer.Tick(TimeSpan.FromMilliseconds(500)));
            Assert.Null(timer.Remaining);
        }

        [Fact]
        public void Set_MitNull_SchaltetAb()
        {
            SleepTimer timer = new();
            timer.Set(TimeSpan.FromMinutes(30));

            timer.Set(null);

            Assert.Null(timer.Remaining);
            Assert.False(timer.Tick(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void Set_WaehrendDesLaufens_BeginntVonVorn()
        {
            SleepTimer timer = new();
            timer.Set(TimeSpan.FromSeconds(10));
            _ = timer.Tick(TimeSpan.FromSeconds(9));

            timer.Set(TimeSpan.FromSeconds(30));

            Assert.Equal(TimeSpan.FromSeconds(30), timer.Remaining);
        }
    }
}
