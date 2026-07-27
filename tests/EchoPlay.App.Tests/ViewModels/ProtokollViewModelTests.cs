using EchoPlay.App.ViewModels;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System.Windows.Input;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="ProtokollViewModel"/> – die Protokollseite der Anwendung.
    /// Geprüft wird alles, was ohne UI-Thread läuft; <c>Activate</c> braucht eine
    /// <c>DispatcherQueue</c> und bleibt deshalb dem Klick-Test am laufenden Programm vorbehalten.
    /// </summary>
    public sealed class ProtokollViewModelTests
    {
        [Fact]
        public void Constructor_WithoutMemorySink_StartsEmptyAndLive()
        {
            using ProtokollViewModel viewModel = new();

            Assert.Empty(viewModel.LogEntries);
            Assert.True(viewModel.IsLiveActive);
        }

        [Fact]
        public void ToggleLiveCommand_SwitchesLiveState()
        {
            using ProtokollViewModel viewModel = new(new MemorySink(capacity: 10));

            viewModel.ToggleLiveCommand.Execute(null);
            Assert.False(viewModel.IsLiveActive);

            viewModel.ToggleLiveCommand.Execute(null);
            Assert.True(viewModel.IsLiveActive);
        }

        [Fact]
        public void ToggleLiveCommand_RaisesPropertyChanged()
        {
            // Mit Puffer: ohne ihn schaltet der Live-Modus bewusst nicht um,
            // weil es nichts nachzuladen gäbe.
            using ProtokollViewModel viewModel = new(new MemorySink(capacity: 10));
            bool notified = false;
            viewModel.PropertyChanged += (_, e) =>
                notified |= e.PropertyName == nameof(ProtokollViewModel.IsLiveActive);

            viewModel.ToggleLiveCommand.Execute(null);

            Assert.True(notified);
        }

        [Fact]
        public void ClearCommand_EmptiesDisplayedEntries()
        {
            using ProtokollViewModel viewModel = new();
            viewModel.LogEntries.Add(new LogEntryViewModel("10:00:00", LogLevel.Information, "Test", "Eine Meldung"));

            viewModel.ClearCommand.Execute(null);

            Assert.Empty(viewModel.LogEntries);
        }

        [Fact]
        public void Deactivate_WithoutActivate_DoesNotThrow()
        {
            // Die Seite ruft Deactivate in OnNavigatedFrom – auch wenn OnNavigatedTo
            // wegen eines Fehlers nie durchlief.
            using ProtokollViewModel viewModel = new(new MemorySink(capacity: 5));

            viewModel.Deactivate();
        }

        [Fact]
        public void Dispose_CalledTwice_IsHarmless()
        {
            ProtokollViewModel viewModel = new(new MemorySink(capacity: 5));

            viewModel.Dispose();
            viewModel.Dispose();
        }

        [Fact]
        public void Commands_AreAvailable()
        {
            using ProtokollViewModel viewModel = new();

            ICommand toggle = viewModel.ToggleLiveCommand;
            ICommand clear = viewModel.ClearCommand;

            Assert.True(toggle.CanExecute(null));
            Assert.True(clear.CanExecute(null));
        }
    }
}
