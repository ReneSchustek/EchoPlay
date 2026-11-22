using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Replication;
using EchoPlay.Data.Context;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            string dbPath = DatabasePathProvider.PrepareDatabasePath();

            // Singleton-Interceptor: PRAGMAs werden bei jeder neuen Verbindung gesetzt.
            // Als Singleton, weil der Interceptor keinen Zustand hält.
            SqlitePragmaInterceptor pragmaInterceptor = new();

            // Geräte-Kennung und Uhr gehören zur Installation: die Kennung als Datei neben der
            // Datenbank, damit ein eingespieltes Paket sie nicht mitbringt.
            services.TryAddSingleton<IClock, SystemClock>();
            _ = services.AddSingleton<IDeviceIdentity>(new FileDeviceIdentity(Path.GetDirectoryName(dbPath)!));
            _ = services.AddSingleton<IHybridClock, HybridLogicalClock>();
            _ = services.AddSingleton<ChangeStampInterceptor>();

            _ = services.AddDbContext<EchoPlayDbContext>((provider, options) =>
                options.UseSqlite($"Data Source={dbPath};Cache=Shared")
                       .AddInterceptors(pragmaInterceptor, provider.GetRequiredService<ChangeStampInterceptor>())
                       .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

            _ = services.AddSingleton(new DatabaseFileLocation(dbPath));
            _ = services.AddScoped<DatabaseInitializer>();

            AddDataServices(services);
            AddReplication(services);
            return services;
        }

        private static void AddDataServices(IServiceCollection services)
        {
            // Registrierung der Services mit Scoped-Lifetime, passend zum DbContext-Lifecycle.
            _ = services.AddScoped<IWatchedTitleDataService, WatchedTitleDataService>();
            _ = services.AddScoped<ISeriesDataService, SeriesDataService>();
            _ = services.AddScoped<IEpisodeDataService, EpisodeDataService>();
            _ = services.AddScoped<IPlaybackStateDataService, PlaybackStateDataService>();
            _ = services.AddScoped<IAppSettingsDataService, AppSettingsDataService>();
            _ = services.AddScoped<ILocalTrackDataService, LocalTrackDataService>();
            _ = services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
            _ = services.AddScoped<IDashboardPositionDataService, DashboardPositionDataService>();
            _ = services.AddScoped<ICachedNewReleaseDataService, CachedNewReleaseDataService>();
            _ = services.AddScoped<IReleaseCalendarDataService, ReleaseCalendarDataService>();
            _ = services.AddScoped<ICoverCopyService, CoverCopyService>();
            _ = services.AddScoped<ICoverImageDataService, CoverImageDataService>();
            _ = services.AddScoped<ISecureSettingsDataService, SecureSettingsDataService>();
            _ = services.AddScoped<IDialogSuppressionDataService, DialogSuppressionDataService>();
            _ = services.AddScoped<ILibraryRootRebaser, LibraryRootRebaser>();
            _ = services.AddScoped<IReplicationPeerDataService>(provider => new ReplicationPeerDataService(provider.GetRequiredService<EchoPlayDbContext>()));
        }

        private static void AddReplication(IServiceCollection services)
        {
            // Umzug: Paket schreiben, Paket prüfen und vorbereiten, Einspielen nach dem Neustart abschließen.
            _ = services.AddScoped<EchoPlay.Data.Replication.PackageExporter>();
            _ = services.AddScoped<EchoPlay.Data.Replication.PackageStager>();
            _ = services.AddScoped<EchoPlay.Data.Replication.ImportFinisher>();

            // Abgleich über einen gemeinsamen Ordner. Die Systemuhr direkt, nicht über den Container:
            // Ein dort eingetragener TimeProvider änderte das Verhalten der Resilience-Pipelines.
            _ = services.AddScoped(provider => new EchoPlay.Data.Replication.Exchange.DeviceReplicationExchange(
                provider.GetRequiredService<EchoPlayDbContext>(),
                provider.GetRequiredService<IDeviceIdentity>(),
                provider.GetRequiredService<IHybridClock>(),
                TimeProvider.System,
                provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>()));
        }
    }
}
