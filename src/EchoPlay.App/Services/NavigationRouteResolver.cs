using EchoPlay.App.Views;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Reine Mapping-Logik für den NavigationService: bildet
    /// <see cref="NavigationTarget"/>-Enum-Werte auf konkrete Page-Typen ab.
    /// Extrahiert aus <see cref="NavigationService"/>, damit die Mapping-Tabelle
    /// ohne Frame-Initialisierung testbar ist.
    /// </summary>
    public static class NavigationRouteResolver
    {
        // Mapping logischer Ziele auf Page-Typen. Single Source of Truth für
        // die Routing-Tabelle der App — neue Seiten werden hier registriert.
        private static readonly Dictionary<NavigationTarget, Type> TargetMap =
            new()
            {
                [NavigationTarget.Dashboard] = typeof(DashboardPage),
                [NavigationTarget.MediathekOnline] = typeof(MediathekOnlinePage),
                [NavigationTarget.MediathekLokal] = typeof(MediathekLokalPage),
                [NavigationTarget.Suche] = typeof(SuchePage),
                [NavigationTarget.Player] = typeof(PlayerPage),
                [NavigationTarget.Settings] = typeof(SettingsPage),
                [NavigationTarget.TagManager] = typeof(TagManagerPage),
                [NavigationTarget.SeriesDetail] = typeof(SeriesDetailPage),
                [NavigationTarget.Import] = typeof(ImportPage),
                [NavigationTarget.Statistik] = typeof(StatistikPage),
                [NavigationTarget.Protokoll] = typeof(ProtokollPage),
                [NavigationTarget.Über] = typeof(UeberPage)
            };

        /// <summary>
        /// Liefert den Page-Typ für ein Navigations-Ziel.
        /// </summary>
        /// <param name="target">Logisches Ziel (Enum-Wert).</param>
        /// <returns>Konkreter Page-Typ.</returns>
        /// <exception cref="KeyNotFoundException">Wenn das Ziel nicht in der Mapping-Tabelle steht.</exception>
        public static Type Resolve(NavigationTarget target)
        {
            if (!TargetMap.TryGetValue(target, out Type? pageType))
            {
                throw new KeyNotFoundException(
                    $"Navigations-Ziel '{target}' ist nicht in der NavigationRouteResolver-Mapping-Tabelle registriert.");
            }
            return pageType;
        }

        /// <summary>
        /// True wenn das Ziel registriert ist.
        /// </summary>
        public static bool IsRegistered(NavigationTarget target) => TargetMap.ContainsKey(target);

        /// <summary>
        /// Anzahl der registrierten Ziele. Wächter für Tests, dass keine
        /// Seite stillschweigend aus der Routing-Tabelle verloren geht.
        /// </summary>
        public static int RegisteredTargetCount => TargetMap.Count;
    }
}
