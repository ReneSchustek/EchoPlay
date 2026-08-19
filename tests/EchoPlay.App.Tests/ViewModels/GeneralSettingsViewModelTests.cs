using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Logger.Models;
using System;
using System.Linq;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Allgemein-Tab der Einstellungen: Werte laden, schreiben und die Grenzen,
    /// die beim Schreiben gezogen werden.
    /// </summary>
    /// <remarks>
    /// Die Grenzen sind kein Schönheitsfehler: Null Tage Aufbewahrung löschte jedes Protokoll
    /// sofort, und ein Neuerscheinungs-Fenster unter einer Woche zeigte praktisch nie etwas.
    /// Beide Werte kommen aus Eingabefeldern und müssen deshalb beim Schreiben gefasst werden.
    /// </remarks>
    public sealed class GeneralSettingsViewModelTests
    {
        [Fact]
        public void Laden_UebernimmtAlleWerteOhneAlsAenderungZuGelten()
        {
            int edits = 0;
            GeneralSettingsViewModel viewModel = new(() => edits++);

            viewModel.LoadFrom(new AppSettings
            {
                ActiveTheme = "PaperCoffee",
                ActiveLanguage = "en",
                NewReleaseDays = 45,
                OfflineMode = true,
                OnlineOnlyMode = true,
                LogRetentionDays = 21,
                MinimumLogLevel = LogLevel.Warning
            });

            Assert.Equal("PaperCoffee", viewModel.ActiveTheme);
            Assert.Equal("en", viewModel.ActiveLanguage);
            Assert.Equal(45, viewModel.NewReleaseDays);
            Assert.True(viewModel.OfflineMode);
            Assert.True(viewModel.OnlineOnlyMode);
            Assert.Equal(21, viewModel.LogRetentionDays);
            Assert.Equal(LogLevel.Warning, viewModel.MinimumLogLevel);
            Assert.Equal(0, edits);
        }

        [Fact]
        public void JedeNutzeraenderung_MeldetSichEinmal()
        {
            int edits = 0;
            GeneralSettingsViewModel viewModel = new(() => edits++);

            viewModel.ActiveTheme = "PaperCoffee";
            viewModel.ActiveLanguage = "en";
            viewModel.NewReleaseDays = 30;
            viewModel.OfflineMode = true;
            viewModel.OnlineOnlyMode = true;
            viewModel.LogRetentionDays = 5;
            viewModel.MinimumLogLevel = LogLevel.Error;

            Assert.Equal(7, edits);
        }

        [Fact]
        public void DerselbeWert_GiltNichtAlsAenderung()
        {
            int edits = 0;
            GeneralSettingsViewModel viewModel = new(() => edits++);
            viewModel.OfflineMode = true;

            viewModel.OfflineMode = true;

            Assert.Equal(1, edits);
        }

        [Fact]
        public void Schreiben_FasstDieGrenzwerte()
        {
            GeneralSettingsViewModel viewModel = new(() => { })
            {
                NewReleaseDays = 3,
                LogRetentionDays = 0
            };

            AppSettings settings = new();
            viewModel.WriteTo(settings);

            Assert.Equal(7, settings.NewReleaseDays);
            Assert.Equal(1, settings.LogRetentionDays);
        }

        [Fact]
        public void Schreiben_KapptEinZuGrossesZeitfenster()
        {
            GeneralSettingsViewModel viewModel = new(() => { }) { NewReleaseDays = 1000 };

            AppSettings settings = new();
            viewModel.WriteTo(settings);

            Assert.Equal(365, settings.NewReleaseDays);
        }

        [Fact]
        public void Schreiben_UebernimmtDieUebrigenWerteUnveraendert()
        {
            GeneralSettingsViewModel viewModel = new(() => { })
            {
                ActiveTheme = "MidnightLibrary",
                ActiveLanguage = "de",
                OfflineMode = true,
                OnlineOnlyMode = false,
                MinimumLogLevel = LogLevel.Debug
            };

            AppSettings settings = new();
            viewModel.WriteTo(settings);

            Assert.Equal("MidnightLibrary", settings.ActiveTheme);
            Assert.Equal("de", settings.ActiveLanguage);
            Assert.True(settings.OfflineMode);
            Assert.False(settings.OnlineOnlyMode);
            Assert.Equal(LogLevel.Debug, settings.MinimumLogLevel);
        }

        [Fact]
        public void Schreiben_OhneEntitaetWirftEineAussagekraeftigeAusnahme()
        {
            GeneralSettingsViewModel viewModel = new(() => { });

            _ = Assert.Throws<ArgumentNullException>(() => viewModel.WriteTo(null!));
            _ = Assert.Throws<ArgumentNullException>(() => viewModel.LoadFrom(null!));
        }

        [Theory]
        [InlineData(0, LogLevel.Trace)]
        [InlineData(1, LogLevel.Debug)]
        [InlineData(2, LogLevel.Information)]
        [InlineData(3, LogLevel.Warning)]
        [InlineData(4, LogLevel.Error)]
        public void Protokollstufe_LaesstSichUeberDenIndexSetzen(int index, LogLevel expected)
        {
            GeneralSettingsViewModel viewModel = new(() => { }) { MinimumLogLevelIndex = index };

            Assert.Equal(expected, viewModel.MinimumLogLevel);
            Assert.Equal(index, viewModel.MinimumLogLevelIndex);
        }

        [Fact]
        public void Sprachauswahl_NenntDeutschUndEnglisch()
        {
            GeneralSettingsViewModel viewModel = new(() => { });

            Assert.Equal(["de", "en"], viewModel.AvailableLanguages.Select(l => l.Code));
        }
    }
}
