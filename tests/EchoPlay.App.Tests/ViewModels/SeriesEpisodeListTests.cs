using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Folgenliste einer Serie: Reiter, Status-Filter, Suche und Sortierung
    /// wirken zusammen, und die Reihenfolge, in der sie greifen, ist Teil des Verhaltens.
    /// </summary>
    public sealed class SeriesEpisodeListTests
    {
        private static EpisodeTileViewModel Tile(
            string title,
            int? episodeNumber = null,
            PlaybackStatus status = PlaybackStatus.NotStarted,
            bool isSpecialEpisode = false,
            DateTime? releaseDate = null,
            bool hasOpenPosition = false)
        {
            return new EpisodeTileViewModel(
                Guid.NewGuid(),
                episodeNumber,
                title,
                totalDuration: TimeSpan.FromMinutes(60),
                status,
                releaseDate,
                playEpisode: () => { },
                isSpecialEpisode: isSpecialEpisode,
                hasOpenPosition: hasOpenPosition);
        }

        private static SeriesEpisodeList BuildList(params EpisodeTileViewModel[] tiles)
        {
            SeriesEpisodeList list = new();
            list.SetSource(tiles);
            return list;
        }

        [Fact]
        public void Reiter_TrenntFolgenVonSonderfolgen()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Folge 1", 1),
                Tile("Weihnachtsfolge", isSpecialEpisode: true));

            Assert.Equal(["Folge 1"], list.Episodes.Select(e => e.Title));
            Assert.True(list.HasSpecialEpisodes);
            Assert.Equal(1, list.SpecialEpisodeCount);

            list.EpisodeTabIndex = 1;

            Assert.Equal(["Weihnachtsfolge"], list.Episodes.Select(e => e.Title));
        }

        [Fact]
        public void Statusfilter_ZeigtUngehoerteGehoerteUndAngefangene()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Ungehört", 1),
                Tile("Gehört", 2, PlaybackStatus.Finished),
                Tile("Läuft", 3, PlaybackStatus.InProgress));

            list.EpisodeFilterIndex = 1;
            Assert.Equal(["Ungehört"], list.Episodes.Select(e => e.Title));

            list.EpisodeFilterIndex = 2;
            Assert.Equal(["Gehört"], list.Episodes.Select(e => e.Title));

            list.EpisodeFilterIndex = 3;
            Assert.Equal(["Läuft"], list.Episodes.Select(e => e.Title));
        }

        [Fact]
        public void Angefangen_ZaehltAuchEineGehoerteFolgeMitOffenerStelle()
        {
            // Wer eine durchgehörte Folge erneut beginnt, findet sie sonst nirgends wieder —
            // und damit auch keinen Weg zurück in die laufende Wiedergabe.
            SeriesEpisodeList list = BuildList(
                Tile("Erneut begonnen", 1, PlaybackStatus.Finished, hasOpenPosition: true),
                Tile("Abgeschlossen", 2, PlaybackStatus.Finished));

            list.EpisodeFilterIndex = 3;

            Assert.Equal(["Erneut begonnen"], list.Episodes.Select(e => e.Title));

            list.EpisodeFilterIndex = 2;

            // Gehört hat man sie trotzdem — sie verschwindet nicht aus „Durchgehört".
            Assert.Equal(2, list.Episodes.Count);
        }

        [Fact]
        public void Suche_IgnoriertGrossUndKleinschreibung()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Der Superhund", 1),
                Tile("Die Fußballfalle", 2));

            list.EpisodeSearchText = "superHUND";

            Assert.Equal(["Der Superhund"], list.Episodes.Select(e => e.Title));
            Assert.True(list.HasActiveFilter);
        }

        [Fact]
        public void SucheUndStatusfilter_WirkenZusammen()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Der Superhund", 1, PlaybackStatus.Finished),
                Tile("Der Schatz", 2));

            list.EpisodeSearchText = "der";
            list.EpisodeFilterIndex = 2;

            Assert.Equal(["Der Superhund"], list.Episodes.Select(e => e.Title));
        }

        [Fact]
        public void OhneTreffer_ErscheintDerHinweisNurBeiAktivemFilter()
        {
            SeriesEpisodeList list = BuildList(Tile("Der Superhund", 1));

            Assert.Equal(Visibility.Collapsed, list.NoResultsVisibility);

            list.EpisodeSearchText = "gibt es nicht";

            Assert.Empty(list.Episodes);
            Assert.Equal(Visibility.Visible, list.NoResultsVisibility);
        }

        [Fact]
        public void Sortierung_NachNummerStelltFolgenOhneNummerHintenAn()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Ohne Nummer"),
                Tile("Zwei", 2),
                Tile("Eins", 1));

            Assert.Equal(["Eins", "Zwei", "Ohne Nummer"], list.Episodes.Select(e => e.Title));
        }

        [Fact]
        public void Sortierung_NachTitelUndDatum()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Beta", 2, releaseDate: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
                Tile("Alpha", 1),
                Tile("Gamma", 3, releaseDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            list.SortOrder = EpisodeSortOrder.Title;
            Assert.Equal(["Alpha", "Beta", "Gamma"], list.Episodes.Select(e => e.Title));

            list.SortOrder = EpisodeSortOrder.ReleaseDate;
            // Ohne Datum steht hinten, nicht vorn
            Assert.Equal(["Gamma", "Beta", "Alpha"], list.Episodes.Select(e => e.Title));
        }

        [Fact]
        public void NeueSerie_SetztReiterUndStatusfilterZurueck()
        {
            // Sonst zeigte eine frisch geöffnete Serie die Einschränkung der vorigen —
            // und wirkte leer, obwohl sie Folgen hat.
            SeriesEpisodeList list = BuildList(
                Tile("Sonderfolge", isSpecialEpisode: true),
                Tile("Folge 1", 1));
            list.EpisodeTabIndex = 1;
            list.EpisodeFilterIndex = 2;

            list.SetSource([Tile("Neue Folge 1", 1)]);

            Assert.Equal(0, list.EpisodeTabIndex);
            Assert.Equal(0, list.EpisodeFilterIndex);
            Assert.Equal(["Neue Folge 1"], list.Episodes.Select(e => e.Title));
            Assert.False(list.HasSpecialEpisodes);
            Assert.Equal(0, list.SpecialEpisodeCount);
        }

        [Fact]
        public void FilterZuruecknehmen_ZeigtWiederAlleFolgen()
        {
            SeriesEpisodeList list = BuildList(
                Tile("Der Superhund", 1),
                Tile("Der Schatz", 2, PlaybackStatus.Finished));
            list.EpisodeSearchText = "Schatz";
            list.EpisodeFilterIndex = 2;

            list.ResetFilters();

            Assert.Equal(string.Empty, list.EpisodeSearchText);
            Assert.Equal(0, list.EpisodeFilterIndex);
            Assert.False(list.HasActiveFilter);
            Assert.Equal(2, list.Episodes.Count);
        }

        [Fact]
        public void OhneQuelle_BleibtDieListeLeer()
        {
            SeriesEpisodeList list = new();

            list.SetSource(null!);

            Assert.Empty(list.Episodes);
            Assert.False(list.HasSpecialEpisodes);
            Assert.Equal(Visibility.Collapsed, list.NoResultsVisibility);
        }

        [Fact]
        public void Aenderungen_MeldenDieAbhaengigenAnzeigen()
        {
            SeriesEpisodeList list = BuildList(Tile("Der Superhund", 1));
            List<string?> gemeldet = [];
            list.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);

            list.EpisodeSearchText = "x";

            Assert.Contains(nameof(SeriesEpisodeList.Episodes), gemeldet);
            Assert.Contains(nameof(SeriesEpisodeList.HasActiveFilter), gemeldet);
            Assert.Contains(nameof(SeriesEpisodeList.NoResultsVisibility), gemeldet);
        }
    }
}
