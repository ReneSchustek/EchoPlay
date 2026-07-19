using EchoPlay.App.Infrastructure;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Statistik-Seite.
    /// Zeigt Kennzahlen zur Sammlung und zum Hörfortschritt.
    /// </summary>
    public sealed class StatistikViewModel : ObservableObject
    {
        private readonly IServiceScopeFactory _scopeFactory;

        private int _seriesCount;
        private int _episodeCount;
        private int _completedCount;
        private int _inProgressCount;
        private string _progressText = string.Empty;
        private double _progressPercent;
        private bool _isLoading;

        /// <summary>
        /// Initialisiert das ViewModel.
        /// </summary>
        /// <param name="scopeFactory">Für scoped DB-Zugriffe.</param>
        public StatistikViewModel(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>Anzahl abonnierter Serien.</summary>
        public int SeriesCount
        {
            get => _seriesCount;
            private set => SetProperty(ref _seriesCount, value);
        }

        /// <summary>Gesamtanzahl aller Episoden.</summary>
        public int EpisodeCount
        {
            get => _episodeCount;
            private set => SetProperty(ref _episodeCount, value);
        }

        /// <summary>Anzahl gehörter Episoden.</summary>
        public int CompletedCount
        {
            get => _completedCount;
            private set => SetProperty(ref _completedCount, value);
        }

        /// <summary>Anzahl angefangener Episoden.</summary>
        public int InProgressCount
        {
            get => _inProgressCount;
            private set => SetProperty(ref _inProgressCount, value);
        }

        /// <summary>Fortschritts-Text, z.B. "42 von 229 Folgen gehört (18%)".</summary>
        public string ProgressText
        {
            get => _progressText;
            private set => SetProperty(ref _progressText, value);
        }

        /// <summary>Fortschritt in Prozent (0–100).</summary>
        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetProperty(ref _progressPercent, value);
        }

        /// <summary>Gibt an ob gerade geladen wird.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Lädt alle Statistiken aus der Datenbank.
        /// </summary>
        public async Task LoadAsync()
        {
            IsLoading = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                IReadOnlyList<Series> allSeries = await seriesService.GetSubscribedAsync();
                IReadOnlyList<PlaybackState> allStates = await stateService.GetAllAsync();

                // Episodenanzahl per Server-Side-GroupBy in einer Query, statt N+1 pro Serie.
                IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                IReadOnlyList<Guid> seriesIds = allSeries.Select(s => s.Id).ToList();
                IReadOnlyDictionary<Guid, (int Total, int Local)> counts =
                    await episodeService.GetEpisodeCountsForSeriesAsync(seriesIds);
                int totalEpisodes = counts.Values.Sum(c => c.Total);

                int completed = allStates.Count(s => s.IsCompleted);
                int inProgress = allStates.Count(s => !s.IsCompleted && s.LastPosition > TimeSpan.Zero);

                SeriesCount = allSeries.Count;
                EpisodeCount = totalEpisodes;
                CompletedCount = completed;
                InProgressCount = inProgress;

                double percent = totalEpisodes > 0 ? (double)completed / totalEpisodes * 100 : 0;
                ProgressPercent = percent;
                ProgressText = totalEpisodes > 0
                    ? $"{completed} von {totalEpisodes} Folgen gehört ({percent:F0}%)"
                    : "Keine Episoden vorhanden";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
