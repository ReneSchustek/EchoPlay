using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Ergebnis der Startup-Validierung, die während des Begrüßungsbildschirms läuft.
    /// Enthält vorgeladene Daten und Statusmeldungen, damit das Dashboard keine
    /// eigenen Checks mehr durchführen muss.
    /// </summary>
    public sealed class StartupResult
    {
        /// <summary>
        /// Gibt an, ob eine Internetverbindung verfügbar ist.
        /// Bei <see langword="false"/> werden Online-Funktionen (Neuerscheinungen, Suche) deaktiviert.
        /// </summary>
        public bool IsOnlineAvailable { get; init; }

        /// <summary>
        /// Gibt an, ob das konfigurierte lokale Bibliotheksverzeichnis erreichbar und lesbar ist.
        /// Bei <see langword="false"/> werden lokale Funktionen (Scan, Sync) temporär deaktiviert.
        /// </summary>
        public bool IsLocalLibraryAvailable { get; init; }

        /// <summary>
        /// Hinweistext, wenn die Online-Verbindung nicht hergestellt werden konnte.
        /// <see langword="null"/> bedeutet: kein Problem oder Online nicht konfiguriert.
        /// </summary>
        public string? OnlineHintText { get; init; }

        /// <summary>
        /// Hinweistext, wenn das lokale Verzeichnis nicht erreichbar ist.
        /// <see langword="null"/> bedeutet: kein Problem oder Lokal nicht konfiguriert.
        /// </summary>
        public string? LocalLibraryHintText { get; init; }

        /// <summary>
        /// Alle abonnierten Serien, vorgeladen während des Splashs.
        /// Das Dashboard greift direkt auf diese Liste zu, ohne erneuten DB-Zugriff.
        /// </summary>
        public IReadOnlyList<Series> SubscribedSeries { get; init; } = [];

        /// <summary>
        /// Bereinigte Neuerscheinungen aus dem Cache (nur überwachte Serien).
        /// </summary>
        public IReadOnlyList<CachedNewRelease> CachedReleases { get; init; } = [];

        /// <summary>
        /// Die beim Start geladenen Anwendungseinstellungen.
        /// </summary>
        public AppSettings Settings { get; init; } = new();

        /// <summary>
        /// Cutoff-Datum für den Neuerscheinungen-Filter (berechnet aus LastAppStart und NewReleaseDays).
        /// </summary>
        public DateTime NewReleaseCutoffDate { get; init; }
    }
}
