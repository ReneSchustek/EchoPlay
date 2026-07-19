namespace EchoPlay.App.Logging
{
    /// <summary>
    /// String-Konstanten fuer Service-Logging-Scopes (Format <c>"Job:&lt;Kategorie&gt;"</c>).
    /// Vermeidet Magic-Strings in <c>BeginScope</c>-Aufrufen und macht die Filter im
    /// Log-Viewer ueber alle Services konsistent.
    /// </summary>
    internal static class JobScopes
    {
        public const string Import = "Job:Import";
        public const string Sync = "Job:Sync";
        public const string Player = "Job:Player";
        public const string UpdateCheck = "Job:UpdateCheck";
        public const string CoverDownload = "Job:CoverDownload";
        public const string OnlineEpisodeCheck = "Job:OnlineEpisodeCheck";
        public const string WatchToggle = "Job:WatchToggle";
        public const string ProviderIdEnrichment = "Job:ProviderIdEnrichment";
        public const string EpisodeCoverCache = "Job:EpisodeCoverCache";
        public const string MissingEpisodes = "Job:MissingEpisodes";
        public const string FolderRestructure = "Job:FolderRestructure";
        public const string ConnectionTest = "Job:ConnectionTest";
        public const string TagLookup = "Job:TagLookup";
    }
}
