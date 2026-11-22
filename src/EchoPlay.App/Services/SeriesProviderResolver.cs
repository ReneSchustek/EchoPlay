using EchoPlay.Data.Entities.Library;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Entscheidet, über welchen Anbieter Delta- und Re-Import einer bestehenden Serie laufen.
    /// </summary>
    /// <param name="logger">Protokoll des Imports.</param>
    internal sealed class SeriesProviderResolver(ILogger logger)
    {
        /// <summary>
        /// Wählt für eine bestehende Serie den Provider, über den Delta-/Reimport laufen sollen.
        /// Spotify wird bevorzugt, weil seine Antworten die Album-Kennungen mitbringen, aber nur,
        /// wenn es auch nutzbar ist: Ohne hinterlegte Credentials
        /// liefert Spotify keinen Token. In dem Fall wird auf Apple Music ausgewichen (öffentliche
        /// iTunes-API, ohne Credentials), sofern die Serie eine Apple-Music-ID hat. Ohne diesen
        /// Fallback lieferten Serien mit SpotifyArtistId ohne Credentials stillschweigend 0 neue
        /// Folgen; neue Folgen kamen nie an, obwohl Apple sie führt.
        /// </summary>
        /// <param name="scope">DI-Scope für den Credential-Store-Lookup.</param>
        /// <param name="series">Die bestehende Serie mit Provider-ID(s).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Provider-Schlüssel und Quell-ID, oder <see langword="null"/> wenn kein nutzbarer Provider vorliegt.</returns>
        public async Task<(string ProviderKey, string SourceSeriesId)?> ResolveAsync(
            IServiceScope scope,
            Series series,
            CancellationToken cancellationToken)
        {
            if (series.SpotifyArtistId is not null)
            {
                ISpotifyClientCredentialsProvider credentialsProvider =
                    scope.ServiceProvider.GetRequiredService<ISpotifyClientCredentialsProvider>();
                SpotifyClientCredentials? credentials = await credentialsProvider.GetAsync(cancellationToken).ConfigureAwait(false);

                if (credentials is not null)
                {
                    return (ProviderKeys.Spotify, series.SpotifyArtistId);
                }

                if (series.AppleMusicArtistId is not null)
                {
                    logger.Warning(
                        "Spotify-Credentials fehlen — \"{Title}\" wird über Apple Music geprüft.", series.Title);
                    return (ProviderKeys.AppleMusic, series.AppleMusicArtistId);
                }

                logger.Warning(
                    "Spotify-Credentials fehlen und keine Apple-Music-ID für \"{Title}\" — Prüfung nicht möglich.", series.Title);
                return null;
            }

            if (series.AppleMusicArtistId is not null)
            {
                return (ProviderKeys.AppleMusic, series.AppleMusicArtistId);
            }

            return null;
        }
    }
}
