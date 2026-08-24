using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using EchoPlay.Logger.Abstractions;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Abschnitt „Ausgeblendete Hinweise" im Reiter Verwaltung.
    /// Listet die dauerhaft ausgeblendeten Dialoge und holt sie einzeln oder gesammelt zurück.
    /// </summary>
    /// <remarks>
    /// Bewusst nicht an <see cref="MaintenanceSettingsViewModel"/> angebaut: Das trägt schon
    /// Datenbankpflege und Protokollansicht. Und der Abschnitt hat nichts zu speichern — er
    /// wirkt sofort und hängt deshalb nicht am gemeinsamen Speichern-Knopf der Seite.
    /// </remarks>
    public sealed class HiddenDialogsViewModel : ObservableObject
    {
        private readonly IDialogSuppressionService _suppressionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;

        private bool _isBusy;

        /// <summary>
        /// Initialisiert das Sub-VM.
        /// </summary>
        /// <param name="suppressionService">Kennt und ändert die ausgeblendeten Dialoge.</param>
        /// <param name="localizationService">Liefert die sprechenden Namen der Dialoge.</param>
        /// <param name="loggerFactory">Fabrik für das Protokoll der nebenher laufenden Schaltflächen.</param>
        public HiddenDialogsViewModel(
            IDialogSuppressionService suppressionService,
            ILocalizationService localizationService,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(suppressionService);
            ArgumentNullException.ThrowIfNull(localizationService);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _suppressionService = suppressionService;
            _localizationService = localizationService;
            _logger = loggerFactory.CreateLogger("HiddenDialogsViewModel");
            Items = [];
        }

        /// <summary>Die derzeit ausgeblendeten Hinweise, jüngste zuerst.</summary>
        public ObservableCollection<HiddenDialogItemViewModel> Items { get; }

        /// <summary>Gibt an, ob überhaupt ein Hinweis ausgeblendet ist.</summary>
        public bool HasEntries => Items.Count > 0;

        /// <summary>Sichtbarkeit der Liste.</summary>
        public Visibility ListVisibility => HasEntries ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Sichtbarkeit des Satzes „zurzeit ist nichts ausgeblendet".</summary>
        public Visibility EmptyHintVisibility => HasEntries ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Gibt an, ob gerade gelesen oder geschrieben wird.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsNotBusy));
                }
            }
        }

        /// <summary>Umgekehrter Zustand für die Aktivierung der Schaltflächen.</summary>
        public bool IsNotBusy => !_isBusy;

        /// <summary>
        /// Liest die ausgeblendeten Hinweise neu ein.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn die Liste steht.</returns>
        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                IReadOnlyList<SuppressedDialogInfo> suppressed = await _suppressionService.GetSuppressedAsync();

                Items.Clear();
                foreach (SuppressedDialogInfo entry in suppressed)
                {
                    Items.Add(CreateItem(entry));
                }
            }
            finally
            {
                IsBusy = false;
                RaiseListState();
            }
        }

        /// <summary>
        /// Holt einen einzelnen Hinweis zurück in die Anzeige.
        /// </summary>
        /// <param name="key">Der zurückzuholende Dialog.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Liste aktualisiert ist.</returns>
        public async Task RestoreAsync(DialogKey key)
        {
            IsBusy = true;
            try
            {
                await _suppressionService.RestoreAsync(key);
            }
            finally
            {
                IsBusy = false;
            }

            await LoadAsync();
        }

        /// <summary>
        /// Holt alle Hinweise zurück in die Anzeige.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn die Liste leer ist.</returns>
        public async Task RestoreAllAsync()
        {
            IsBusy = true;
            try
            {
                await _suppressionService.RestoreAllAsync();
            }
            finally
            {
                IsBusy = false;
            }

            await LoadAsync();
        }

        private HiddenDialogItemViewModel CreateItem(SuppressedDialogInfo entry)
        {
            string name = _localizationService.Get($"DialogName_{entry.Key}");
            string when = entry.SuppressedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

            return new HiddenDialogItemViewModel(
                entry.Key,
                name,
                when,
                entry.Key.WritesFilesWithoutUndo(),
                new RelayCommand(() => DetachedTask.Observe(RestoreAsync(entry.Key), _logger)));
        }

        private void RaiseListState()
        {
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(ListVisibility));
            OnPropertyChanged(nameof(EmptyHintVisibility));
        }
    }
}
