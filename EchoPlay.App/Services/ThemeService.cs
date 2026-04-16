using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Verwaltet das aktive Farbthema der Anwendung.
    /// Lädt beim Start das gespeicherte Theme, entfernt die inaktiven aus den MergedDictionaries
    /// und tauscht sie bei Theme-Wechsel zur Laufzeit aus.
    /// Die ResourceDictionary-Instanzen werden beim Start gecacht, damit der Wechsel
    /// kein erneutes XAML-Parsen auslöst und zuverlässig funktioniert.
    /// Muss vom UI-Thread aufgerufen werden.
    /// </summary>
    public sealed class ThemeService : IThemeService
    {
        /// <summary>
        /// Alle bekannten Theme-Namen – entsprechen den Dateinamen in EchoPlay.App/Themes/.
        /// </summary>
        private static readonly IReadOnlyList<string> KnownThemes =
            ["Ruhrcoder", "ModernClassic", "MidnightLibrary", "PaperCoffee", "ForestSignal", "AmberWhiskey"];

        /// <summary>
        /// Themes mit fester dunkler Palette – ThemeService setzt RequestedTheme auf Dark,
        /// damit WinUI die richtige ThemeDictionary-Sektion auswählt.
        /// </summary>
        private static readonly HashSet<string> DarkThemes =
            new(StringComparer.OrdinalIgnoreCase)
            { "MidnightLibrary", "ForestSignal", "AmberWhiskey" };

        // ThemeService ist Singleton, IAppSettingsDataService ist Scoped – daher pro Zugriff
        // einen eigenen DI-Scope öffnen, damit der DbContext nicht als Captive gehalten wird.
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Gecachte ResourceDictionary-Instanzen je Theme-Name.
        /// App.xaml lädt alle sechs Themes vorab – wir halten die Instanzen hier,
        /// damit beim Wechsel kein erneutes XAML-Parsen nötig ist.
        /// </summary>
        private readonly Dictionary<string, ResourceDictionary> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        private ResourceDictionary? _activeThemeDictionary;
        private string _activeThemeName = "Ruhrcoder";

        /// <summary>
        /// Initialisiert den ThemeService.
        /// </summary>
        /// <param name="scopeFactory">Scope-Fabrik für kurzlebige <see cref="IAppSettingsDataService"/>-Zugriffe.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public ThemeService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger(nameof(ThemeService));
        }

        /// <summary>
        /// Lädt das in AppSettings gespeicherte Theme und wendet es an.
        /// Unbekannte Theme-Namen fallen auf "Ruhrcoder" zurück.
        /// Muss einmalig beim App-Start vom UI-Thread aufgerufen werden, bevor das Fenster sichtbar wird.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task InitializeAsync()
        {
            using LogScope logScope = _logger.BeginScope(nameof(InitializeAsync));

            AppSettings settings;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                settings = await settingsService.GetAsync();
            }

            // Unbekannte Namen (z.B. durch alte Datenbank-Einträge) auf den Default zurückfallen lassen
            string themeName = KnownThemes.Contains(settings.ActiveTheme)
                ? settings.ActiveTheme
                : "Ruhrcoder";

            // App.xaml hat alle sechs Themes vorab geladen – Instanzen cachen, bevor wir die inaktiven entfernen.
            // Beim späteren Theme-Wechsel können wir die gecachte Instanz direkt wiederverwenden,
            // ohne das XAML nochmals parsen zu müssen.
            CachePreloadedDictionaries();
            RemoveAllThemeDictionaries();
            LoadAndApplyTheme(themeName);

            _logger.Info($"Theme initialisiert: {themeName}");
        }

        /// <summary>
        /// Wechselt das aktive Theme zur Laufzeit und persistiert die Auswahl.
        /// Unbekannte Namen werden ignoriert und geloggt.
        /// </summary>
        /// <param name="themeName">Name des gewünschten Themes. Gültig: "Ruhrcoder", "ModernClassic", "MidnightLibrary", "PaperCoffee", "ForestSignal", "AmberWhiskey".</param>
        public void ApplyTheme(string themeName)
        {
            if (!KnownThemes.Contains(themeName))
            {
                _logger.Warning($"Unbekanntes Theme ignoriert: {themeName}");
                return;
            }

            if (_activeThemeName == themeName)
            {
                return;
            }

            if (_activeThemeDictionary is not null)
            {
                _ = Application.Current.Resources.MergedDictionaries.Remove(_activeThemeDictionary);
                _activeThemeDictionary = null;
            }

            // Namen vor dem Aufruf sichern – LoadAndApplyTheme überschreibt _activeThemeName
            string previousThemeName = _activeThemeName;
            LoadAndApplyTheme(themeName);

            _logger.Info($"Theme gewechselt: {previousThemeName} → {themeName}");

            // Persistierung im Hintergrund – ein Fehler darf den Theme-Wechsel nicht blockieren
            _ = PersistThemeAsync(themeName);
        }

        /// <summary>
        /// Gibt den Namen des aktuell aktiven Themes zurück.
        /// </summary>
        /// <returns>Theme-Name, z.B. "MidnightLibrary".</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Bewusst als Methode modelliert, da ThemeService kuenftig asynchron nachladen koennte (ActiveThemeName via Get-Methode bleibt stabil, Property-Umwandlung waere Breaking Change).")]
        public string GetActiveThemeName() => _activeThemeName;

        /// <summary>
        /// Setzt <c>RequestedTheme</c> am Root-Element des Hauptfensters auf den zum aktiven Theme passenden Wert.
        /// Muss nach der Fenstererstellung aufgerufen werden, da <c>InitializeAsync</c> vor dem Erstellen von
        /// <see cref="App.MainWindow"/> läuft und <c>Content</c> dort noch <see langword="null"/> ist.
        /// </summary>
        public void SyncRequestedTheme()
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                root.RequestedTheme = DarkThemes.Contains(_activeThemeName)
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
        }

        /// <summary>
        /// Durchsucht die aktuellen MergedDictionaries nach den bekannten Theme-Einträgen
        /// und speichert ihre Instanzen im Cache.
        /// App.xaml lädt alle sechs Themes vorab – dieser Cache verhindert späteres XAML-Parsen.
        /// </summary>
        private void CachePreloadedDictionaries()
        {
            IList<ResourceDictionary> merged = Application.Current.Resources.MergedDictionaries;

            foreach (ResourceDictionary dict in merged)
            {
                if (dict.Source is null)
                {
                    continue;
                }

                string sourceString = dict.Source.ToString();

                foreach (string name in KnownThemes)
                {
                    if (sourceString.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        _cache[name] = dict;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Fügt die ResourceDictionary des angegebenen Themes zu den MergedDictionaries hinzu
        /// und setzt <c>RequestedTheme</c> am Root-Element entsprechend der Theme-Helligkeit.
        /// Verwendet die gecachte Instanz, falls vorhanden – andernfalls wird ein neues Dictionary erzeugt.
        /// </summary>
        /// <param name="themeName">Name des Themes.</param>
        private void LoadAndApplyTheme(string themeName)
        {
            // Gecachte Instanz wiederverwenden – die ResourceDictionary wurde beim App-Start bereits geparst.
            // Nur wenn kein Cache-Eintrag existiert, wird ein neues Dictionary erzeugt.
            if (!_cache.TryGetValue(themeName, out ResourceDictionary? dict))
            {
                dict = new() { Source = new($"ms-appx:///Themes/{themeName}.xaml") };
            }

            Application.Current.Resources.MergedDictionaries.Add(dict);
            _activeThemeDictionary = dict;
            _activeThemeName = themeName;

            // RequestedTheme steuert, welche ThemeDictionary-Sektion (Light/Dark) WinUI auswählt.
            // Beim Start ist Content noch null (Fenster wurde noch nicht erstellt) –
            // SyncRequestedTheme() übernimmt das Setzen nach der Fenstererstellung.
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                ElementTheme targetTheme = DarkThemes.Contains(themeName)
                    ? ElementTheme.Dark
                    : ElementTheme.Light;

                // Kurz auf das Gegenteil schalten, damit WinUI alle ThemeResource-Bindungen
                // neu auswertet – auch wenn der Zielwert identisch mit dem vorherigen ist
                // (z.B. beim Wechsel zwischen zwei hellen oder zwei dunklen Themes).
                // Beide Zuweisungen erfolgen synchron in einem Layout-Pass, kein Flackern.
                root.RequestedTheme = targetTheme == ElementTheme.Dark
                    ? ElementTheme.Light
                    : ElementTheme.Dark;
                root.RequestedTheme = targetTheme;
            }
        }

        /// <summary>
        /// Entfernt alle bekannten Theme-Dictionaries aus den MergedDictionaries.
        /// Wird beim Start aufgerufen, da App.xaml alle sechs Paletten vorlädt.
        /// </summary>
        private static void RemoveAllThemeDictionaries()
        {
            IList<ResourceDictionary> merged = Application.Current.Resources.MergedDictionaries;

            // Dictionaries identifizieren anhand der Source-URI (enthält den Theme-Namen)
            List<ResourceDictionary> toRemove = merged
                .Where(d => d.Source is not null
                    && KnownThemes.Any(name => d.Source.ToString().Contains(name,
                        StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (ResourceDictionary dict in toRemove)
            {
                _ = merged.Remove(dict);
            }
        }

        /// <summary>
        /// Speichert den Theme-Namen in AppSettings.
        /// Fehler werden geloggt und unterdrückt, da die Persistenz den Betrieb nicht blockieren darf.
        /// </summary>
        /// <param name="themeName">Zu speichernder Theme-Name.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Theme-Persistenz: DbContext-, Migration- oder Concurrency-Fehler beim Schreiben der AppSettings duerfen den UI-Theme-Wechsel nicht blockieren und werden lediglich geloggt.")]
        private async Task PersistThemeAsync(string themeName)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync();
                settings.ActiveTheme = themeName;
                await settingsService.SaveAsync(settings);
            }
            catch (Exception ex)
            {
                _logger.Error($"Theme konnte nicht persistiert werden: {themeName}", ex);
            }
        }
    }
}
