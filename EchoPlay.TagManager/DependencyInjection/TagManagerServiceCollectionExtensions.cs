using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.TagManager.DependencyInjection
{
    /// <summary>
    /// Registriert alle Dienste des <c>EchoPlay.TagManager</c>-Projekts im DI-Container.
    /// </summary>
    public static class TagManagerServiceCollectionExtensions
    {
        /// <summary>
        /// Fügt <see cref="ITagService"/> und <see cref="ITagLookupService"/> dem <paramref name="services"/>-Container hinzu.
        /// Der MusicBrainz-HTTP-Client wird mit Base-URL und User-Agent vorkonfiguriert,
        /// damit die Nutzungsbedingungen (eindeutiger User-Agent, 1 req/s) eingehalten werden können.
        /// </summary>
        /// <param name="services">Der <see cref="IServiceCollection"/>, in den die Dienste eingetragen werden.</param>
        /// <returns>
        /// Den übergebenen <paramref name="services"/> für Method-Chaining.
        /// </returns>
        public static IServiceCollection AddTagManager(this IServiceCollection services)
        {
            _ = services.AddTransient<ITagService, TagService>();
            _ = services.AddTransient<IFileRenameService, FileRenameService>();

            // MusicBrainz erwartet einen eindeutigen User-Agent im Format
            // "AppName/Version (contact@example.com)" – sonst droht ein 403.
            _ = services.AddHttpClient<ITagLookupService, MusicBrainzLookupService>(client =>
            {
                client.BaseAddress = new Uri("https://musicbrainz.org/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EchoPlay/1.0 (echoplay@ruhrcoder.de)");
            });

            return services;
        }
    }
}
