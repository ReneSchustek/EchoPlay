using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Der dezente Hinweis zum Abgleich in der Statusleiste: während ein Durchgang läuft, und wenn
    /// der letzte gescheitert ist oder ein Gerät nicht gelesen werden konnte. Sonst unsichtbar.
    /// </summary>
    /// <remarks>
    /// Eigenes Ansichtsmodell statt eines weiteren Abschnitts im ohnehin langen
    /// <see cref="StatusBarViewModel"/>. Der Dienst meldet sich aus seinem Hintergrund-Thread; die
    /// Meldung geht über die Warteschlange des Fensters.
    /// </remarks>
    public sealed class ReplicationStatusViewModel : ObservableObject
    {
        private readonly DeviceReplicationService _service;
        private readonly ILocalizationService _text;
        private readonly DispatcherQueue? _dispatcher;
        private string _hintText = string.Empty;
        private string _detailText = string.Empty;

        /// <summary>
        /// Initialisiert den Hinweis und meldet ihn beim Dienst an.
        /// </summary>
        /// <param name="service">Der Abgleich im Hintergrund.</param>
        /// <param name="text">Liefert die Anzeigetexte.</param>
        /// <param name="dispatcher">Warteschlange des Fensters; ohne sie wird direkt aktualisiert.</param>
        internal ReplicationStatusViewModel(DeviceReplicationService service, ILocalizationService text, DispatcherQueue? dispatcher)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _dispatcher = dispatcher;
            _service.StateChanged += OnStateChanged;
            Refresh();
        }

        /// <summary>Der kurze Hinweis, oder leer.</summary>
        public string HintText
        {
            get => _hintText;
            private set
            {
                if (SetProperty(ref _hintText, value))
                {
                    OnPropertyChanged(nameof(HintVisibility));
                }
            }
        }

        /// <summary>Die Einzelheiten für den Tooltip.</summary>
        public string DetailText
        {
            get => _detailText;
            private set => SetProperty(ref _detailText, value);
        }

        /// <summary>Sichtbarkeit des Hinweises.</summary>
        public Visibility HintVisibility => string.IsNullOrEmpty(_hintText) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Liest den Stand des Dienstes und setzt den Hinweis.</summary>
        internal void Refresh()
        {
            if (_service.IsRunning)
            {
                HintText = _text.Get("ReplicationStatusRunning");
                DetailText = string.Empty;
                return;
            }

            ReplicationRun? run = _service.LastRun;
            string details = run is null ? string.Empty : run.Error ?? string.Join(Environment.NewLine, run.Received.Problems);
            HintText = details.Length == 0 ? string.Empty : _text.Get("ReplicationStatusProblem");
            DetailText = details;
        }

        private void OnStateChanged()
        {
            if (_dispatcher is null || !_dispatcher.TryEnqueue(Refresh))
            {
                Refresh();
            }
        }
    }
}
