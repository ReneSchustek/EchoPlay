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

            services.AddDbContext<EchoPlayDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath};Cache=Shared")
                       .AddInterceptors(pragmaInterceptor)
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

            services.AddScoped<DatabaseInitializer>();

            // Registrierung der Services mit Scoped-Lifetime, passend zum DbContext-Lifecycle.
            services.AddScoped<ISeriesDataService, SeriesDataService>();
            services.AddScoped<IEpisodeDataService, EpisodeDataService>();
            services.AddScoped<IPlaybackStateDataService, PlaybackStateDataService>();
            services.AddScoped<IAppSettingsDataService, AppSettingsDataService>();
            services.AddScoped<ILocalTrackDataService, LocalTrackDataService>();
            services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
            services.AddScoped<IDashboardPositionDataService, DashboardPositionDataService>();
            services.AddScoped<ICachedNewReleaseDataService, CachedNewReleaseDataService>();
            services.AddScoped<ICoverCopyService, CoverCopyService>();
            services.AddScoped<ICoverImageDataService, CoverImageDataService>();

            return services;
        }
    }
}