using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für das Hauptfenster. Verwaltet die Shell-Ebene der App:
    /// Sichtbarkeit der Navigationseinträge auf Basis der AppSettings sowie
    /// die zentrale Navigation über den <see cref="INavigationService"/>.
    /// Der Code-Behind von MainWindow bleibt dadurch auf reine UI-Adapter
    /// (Slider-Feedback, HWND-Beschaffung, Glyph-Wechsel) beschränkt.
    /// </summary>
    public sealed class MainWindowViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly INavigationService _navigationService;

        private bool _isMediathekOnlineVisible;
        private bool _isMediathekLokalVisible = true;
        private bool _isTagManagerVisible = true;

        /// <summary>
        /// Erstellt das ViewModel. Alle Abhängigkeiten kommen per DI.
        /// </summary>
        /// <param name="scopeFactory">Scope-Factory für den Zugriff auf <see cref="IAppSettingsDataService"/>.</param>
        /// <param name="navigationService">Zentraler Navigationsdienst.</param>
        public MainWindowViewModel(
            IServiceScopeFactory scopeFactory,
            INavigationService navigationService)
        {
            _scopeFactory = scopeFactory;
            _navigationService = navigationService;
        }

        /// <summary>
        /// Ob der Online-Mediathek-Eintrag in der NavigationView sichtbar ist.
        /// Entspricht „<see cref="AppSettings.ActiveProvider"/> != <see cref="ProviderType.None"/>".
        /// </summary>
        public bool IsMediathekOnlineVisible
        {
            get => _isMediathekOnlineVisible;
            private set => SetProperty(ref _isMediathekOnlineVisible, value);
        }

        /// <summary>
        /// Ob der Lokal-Mediathek-Eintrag sichtbar ist.
        /// Im Nur-Online-Modus (<see cref="AppSettings.OnlineOnlyMode"/>) wird er ausgeblendet.
        /// </summary>
        public bool IsMediathekLokalVisible
        {
            get => _isMediathekLokalVisible;
            private set => SetProperty(ref _isMediathekLokalVisible, value);
        }

        /// <summary>
        /// Ob der Tag-Manager-Eintrag sichtbar ist.
        /// Im Nur-Online-Modus wird er ebenfalls ausgeblendet, da ohne lokale Dateien sinnlos.
        /// </summary>
        public bool IsTagManagerVisible
        {
            get => _isTagManagerVisible;
            private set => SetProperty(ref _isTagManagerVisible, value);
        }

        /// <summary>
        /// Lädt die AppSettings und aktualisiert die Sichtbarkeits-Properties.
        /// Wird einmalig beim Laden des Fensters aufgerufen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Initiales Laden der NavigationView-Sichtbarkeiten: DB-Fehler beim Laden der AppSettings dürfen das MainWindow nicht blockieren – Defaultwerte werden beibehalten und der Fehler lediglich geloggt.")]
        public async Task LoadAsync()
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService =
                    scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();

                IsMediathekOnlineVisible = settings.ActiveProvider != ProviderType.None;

                bool hideLocal = settings.OnlineOnlyMode;
                IsMediathekLokalVisible = !hideLocal;
                IsTagManagerVisible = !hideLocal;
            }
            catch
            {
                // Bei Fehler: Online-Eintrag sicherheitshalber verstecken,
                // lokale Einträge bleiben sichtbar (konservativer Fallback).
                IsMediathekOnlineVisible = false;
            }
        }

        /// <summary>
        /// Navigiert zur initialen Startseite (Dashboard). Wird beim ersten Laden aufgerufen.
        /// </summary>
        public void NavigateToStart()
        {
            _navigationService.NavigateTo(NavigationTarget.Dashboard);
        }

        /// <summary>
        /// Navigiert zu einem Menüpunkt der NavigationView. Der Tag-String des
        /// ausgewählten Items bestimmt das Ziel.
        /// </summary>
        /// <param name="menuTag">Tag-String des NavigationViewItem.</param>
        /// <returns><c>true</c> wenn das Tag einem bekannten Ziel entspricht.</returns>
        public bool NavigateToMenuTag(string? menuTag)
        {
            NavigationTarget? target = menuTag switch
            {
                "Startseite" => NavigationTarget.Dashboard,
                "MediathekOnline" => NavigationTarget.MediathekOnline,
                "MediathekLokal" => NavigationTarget.MediathekLokal,
                "TagManager" => NavigationTarget.TagManager,
                "Suche" => NavigationTarget.Suche,
                "Player" => NavigationTarget.Player,
                "Statistik" => NavigationTarget.Statistik,
                "Über" => NavigationTarget.Über,
                _ => null
            };

            if (target is null)
            {
                return false;
            }

            _navigationService.NavigateTo(target.Value);
            return true;
        }

        /// <summary>Navigiert zur Einstellungsseite (Settings-Eintrag der NavigationView).</summary>
        public void NavigateToSettings()
        {
            _navigationService.NavigateTo(NavigationTarget.Settings);
        }

        /// <summary>
        /// Geht eine Seite zurück, falls möglich.
        /// </summary>
        /// <returns><c>true</c> wenn tatsächlich zurück gesprungen wurde.</returns>
        public bool GoBack() => _navigationService.GoBack();
    }
}
