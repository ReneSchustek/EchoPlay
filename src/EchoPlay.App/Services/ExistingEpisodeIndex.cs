using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Der vorhandene Folgenbestand einer Serie, nachschlagbar nach Anbieter-Kennung und Titel.
    /// Import, Re-Import und Delta-Abgleich stellen ihm dieselbe Frage: „Kenne ich diese Folge schon?"
    /// </summary>
    /// <remarks>
    /// Die Anbieter-Kennung (Album-ID) entscheidet zuerst – sie bleibt stabil, auch wenn der
    /// Anbieter den Albumnamen später ändert. Der Titel ist der Rückfall für Folgen, die ohne
    /// Kennung in der Datenbank stehen: lokal eingelesene und solche aus älteren Importen.
    /// <para>
    /// Neu aufgenommene Folgen wandern über <see cref="Add"/> sofort in den Index. Damit legt
    /// auch eine Anbieter-Antwort, die dieselbe Folge zweimal führt, nur eine Zeile an.
    /// </para>
    /// </remarks>
    internal sealed class ExistingEpisodeIndex
    {
        private readonly Dictionary<string, Episode> _byProviderId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Episode> _byTitle = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _titles = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Baut den Index über den vorhandenen Bestand auf.
        /// </summary>
        /// <param name="episodes">Die bereits gespeicherten Folgen der Serie.</param>
        public ExistingEpisodeIndex(IReadOnlyList<Episode> episodes)
        {
            ArgumentNullException.ThrowIfNull(episodes);

            foreach (Episode episode in episodes)
            {
                Add(episode);
            }
        }

        /// <summary>
        /// Die bekannten Titel. Der Anbieter spart damit den teuren Track-Abruf für Folgen,
        /// die ohnehin schon vorliegen.
        /// </summary>
        public IReadOnlySet<string> Titles => _titles;

        /// <summary>
        /// Nimmt eine Folge in den Index auf. Der erste Eintrag je Kennung bzw. Titel gewinnt –
        /// so trifft ein späteres Nachschlagen bei doppelten Titeln immer denselben Datensatz.
        /// </summary>
        /// <param name="episode">Die aufzunehmende Folge.</param>
        public void Add(Episode episode)
        {
            ArgumentNullException.ThrowIfNull(episode);

            if (!string.IsNullOrEmpty(episode.AppleMusicAlbumId))
            {
                _ = _byProviderId.TryAdd(episode.AppleMusicAlbumId, episode);
            }

            if (!string.IsNullOrEmpty(episode.SpotifyAlbumId))
            {
                _ = _byProviderId.TryAdd(episode.SpotifyAlbumId, episode);
            }

            _ = _byTitle.TryAdd(episode.Title, episode);
            _ = _titles.Add(episode.Title);
        }

        /// <summary>
        /// Sucht die gespeicherte Folge zu einem Anbieter-Treffer.
        /// </summary>
        /// <param name="importEpisode">Der Treffer des Anbieters.</param>
        /// <param name="existing">Die gefundene Folge, sonst <see langword="null"/>.</param>
        /// <returns><see langword="true"/>, wenn die Folge bereits im Bestand liegt.</returns>
        public bool TryFind(ImportEpisode importEpisode, [NotNullWhen(true)] out Episode? existing)
        {
            ArgumentNullException.ThrowIfNull(importEpisode);

            if (_byProviderId.TryGetValue(importEpisode.SourceEpisodeId, out existing))
            {
                return true;
            }

            return _byTitle.TryGetValue(importEpisode.Title, out existing);
        }

        /// <summary>
        /// Ob der Bestand diesen Anbieter-Treffer bereits kennt.
        /// </summary>
        /// <param name="importEpisode">Der Treffer des Anbieters.</param>
        public bool Contains(ImportEpisode importEpisode) => TryFind(importEpisode, out _);
    }
}
