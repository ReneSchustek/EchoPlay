using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Konstruktor-Tests für <see cref="ThemeService"/>.
    /// InitializeAsync/ApplyTheme/PersistThemeAsync berühren Application.Current und App.MainWindow –
    /// diese WinUI-3-Pfade sind im Unit-Test-Host nicht isolierbar und werden daher bewusst ausgespart.
    /// </summary>
    public sealed class ThemeServiceTests
    {
        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenScopeFactoryIsNull()
        {
            ILoggerFactory loggerFactory = new FakeLoggerFactory();

            _ = Assert.Throws<ArgumentNullException>(() =>
                new ThemeService(scopeFactory: null!, loggerFactory));
        }

        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerFactoryIsNull()
        {
            IServiceScopeFactory scopeFactory = BuildScopeFactory();

            _ = Assert.Throws<ArgumentNullException>(() =>
                new ThemeService(scopeFactory, loggerFactory: null!));
        }

        [Fact]
        public void Constructor_AcceptsScopeFactory_WithoutResolvingSettingsServiceEagerly()
        {
            // Der Fake wird nur registriert, aber nicht gebaut – der Konstruktor darf keinen Scope
            // öffnen, sonst wäre die Captive-Dependency-Auflösung wirkungslos.
            ServiceCollection services = new();
            int settingsBuilds = 0;
            _ = services.AddScoped<IAppSettingsDataService>(_ =>
            {
                settingsBuilds++;
                return new FakeAppSettingsDataService();
            });
            ServiceProvider provider = services.BuildServiceProvider();

            ThemeService service = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLoggerFactory());

            Assert.NotNull(service);
            Assert.Equal(0, settingsBuilds);
        }

        private static IServiceScopeFactory BuildScopeFactory()
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());
            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }
    }
}
