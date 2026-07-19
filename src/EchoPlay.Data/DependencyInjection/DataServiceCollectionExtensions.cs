using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.Data.DependencyInjection
{
    /// <summary>
    /// IServiceCollection-Erweiterungen für die Registrierung der Daten-Schicht.
    /// </summary>
    public static class DataServiceCollectionExtensions
    {
        /// <summary>
        /// Registriert alle Datenbank-Kontexte und Daten-Services im DI-Container.
        /// </summary>
        /// <param name="services">Die Service-Sammlung.</param>
        /// <returns>Die modifizierte Service-Sammlung für Method-Chaining.</returns>
        public static IServiceCollection AddEchoPlayData(this IServiceCollection services)
        {
            string dbPath = DatabasePathProvider.GetDatabasePath();

            // Singleton-Interceptor: PRAGMAs werden bei jeder neuen Verbindung gesetzt.
            // Als Singleton, weil der Interceptor keinen Zustand hält.
            SqlitePragmaInterceptor pragmaInterceptor = new();

            _ = services.AddDbContext<EchoPlayDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath};Cache=Shared")
                       .AddInterceptors(pragmaInterceptor)
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

            _ = services.AddScoped<DatabaseInitializer>();

            // Registrierung der Services mit Scoped-Lifetime, passend zum DbContext-Lifecycle.
            _ = services.AddScoped<ISeriesDataService, SeriesDataService>();
            _ = services.AddScoped<IEpisodeDataService, EpisodeDataService>();
            _ = services.AddScoped<IPlaybackStateDataService, PlaybackStateDataService>();
            _ = services.AddScoped<IAppSettingsDataService, AppSettingsDataService>();
            _ = services.AddScoped<ILocalTrackDataService, LocalTrackDataService>();
            _ = services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
            _ = services.AddScoped<IDashboardPositionDataService, DashboardPositionDataService>();
            _ = services.AddScoped<ICachedNewReleaseDataService, CachedNewReleaseDataService>();
            _ = services.AddScoped<ICoverCopyService, CoverCopyService>();
            _ = services.AddScoped<ICoverImageDataService, CoverImageDataService>();
            _ = services.AddScoped<ISecureSettingsDataService, SecureSettingsDataService>();

            return services;
        }
    }
}
