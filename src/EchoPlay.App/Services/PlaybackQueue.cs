using EchoPlay.Logger.Abstractions;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die Abspielliste der laufenden Folge samt ihrer Quellen.
    /// </summary>
    /// <remarks>
    /// Die <see cref="MediaPlaybackList"/> gibt ihre Quellen beim Leeren nicht frei; jede hält ihre
    /// Datei offen, bis der Garbage Collector sie findet. Deshalb führt diese Klasse sie selbst und
    /// schließt sie, sobald sie die Liste verlassen.
    /// </remarks>
    internal sealed class PlaybackQueue : IDisposable
    {
        /// <summary>Fehlercode von Media Foundation für eine Anforderung im falschen Zustand.</summary>
        private const int InvalidRequestHResult = unchecked((int)0xC00D36B2);

        // Hält Prüfung und Leeren im Hintergrund-Stopp mit dem Befüllen zusammen. Ohne die Sperre
        // könnte eine neue Folge genau zwischen Prüfung und Leeren eingelegt werden, und der Stopp
        // leerte sie wieder. Die Handler der Liste reichen nur per TryEnqueue an die Oberfläche
        // weiter und warten nie auf sie; die Sperre kann sich daher nicht verklemmen.
        private readonly object _gate = new();

        private List<MediaSource> _sources = [];

        // Zählt jedes Neubefüllen. Der Stopp läuft im Hintergrund nach; trifft er auf eine
        // inzwischen neu gestartete Folge, darf er deren Liste nicht mehr anfassen.
        private int _generation;

        /// <summary>Die Liste, die dem <see cref="MediaPlayer"/> als Quelle dient.</summary>
        public MediaPlaybackList List { get; } = new();

        /// <summary>Die Quellen der laufenden Liste.</summary>
        public IReadOnlyList<MediaSource> Sources => Volatile.Read(ref _sources);

        /// <summary>Anzahl der Einträge in der Liste.</summary>
        public int Count => List.Items.Count;

        /// <summary>Die laufende Spur, nullbasiert. Ohne laufende Wiedergabe null.</summary>
        public int CurrentIndex
        {
            get
            {
                uint index = List.CurrentItemIndex;

                // Die Abspielliste meldet zwischen zwei Titeln einen unbelegten Index.
                return index == uint.MaxValue ? 0 : (int)index;
            }
        }

        /// <summary>
        /// Füllt die Liste mit den Spuren der neuen Folge und schließt die Quellen der vorigen.
        /// </summary>
        /// <param name="trackPaths">Dateipfade der Spuren in Abspielreihenfolge.</param>
        public void Load(IReadOnlyList<string> trackPaths)
        {
            lock (_gate)
            {
                _generation++;
                List.Items.Clear();

                // Die neue Liste steht im Feld, bevor die erste Quelle entsteht: Scheitert eine
                // Adresse mittendrin, bleiben die schon geöffneten Quellen erreichbar und werden
                // beim nächsten Wechsel oder beim Beenden geschlossen.
                List<MediaSource> sources = new(trackPaths.Count);
                DisposeSources(Interlocked.Exchange(ref _sources, sources));

                foreach (string path in trackPaths)
                {
                    MediaSource source = MediaSource.CreateFromUri(new Uri(path));
                    sources.Add(source);
                    List.Items.Add(new MediaPlaybackItem(source));
                }
            }
        }

        /// <summary>
        /// Legt die Spur fest, mit der die Wiedergabe beginnt; negative Werte gelten als erste Spur.
        /// </summary>
        /// <remarks>
        /// Die Startspur steht immer fest, der Sprung dorthin gelingt nur bei geöffneter Liste.
        /// Direkt nach dem Befüllen ist sie oft noch nicht offen, auf einem schnellen Rechner fast
        /// immer; Media Foundation weist den Sprung dann zurück, und ohne Startspur scheiterte der
        /// ganze Start.
        /// </remarks>
        /// <param name="index">Die Spur, nullbasiert.</param>
        /// <returns><see langword="false"/>, wenn die Liste noch nicht offen war und die Startspur gilt.</returns>
        public bool StartAt(int index)
        {
            uint target = (uint)Math.Max(0, index);
            if (target < List.Items.Count)
            {
                List.StartingItem = List.Items[(int)target];
            }

            try
            {
                _ = List.MoveTo(target);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == InvalidRequestHResult)
            {
                return false;
            }
        }

        /// <summary>Springt zur nächsten Spur.</summary>
        public void MoveNext() => _ = List.MoveNext();

        /// <summary>Springt zur vorigen Spur.</summary>
        public void MovePrevious() => _ = List.MovePrevious();

        /// <summary>
        /// Leert die Liste auf einem Hintergrund-Thread und schließt ihre Quellen, sofern sie bis
        /// dahin nicht neu befüllt wurde.
        /// </summary>
        /// <param name="beforeClear">Läuft unter derselben Sperre unmittelbar vor dem Leeren.</param>
        /// <param name="logger">Nimmt Fehler der Pipeline auf.</param>
        /// <returns>Der Hintergrundschritt; er wirft nie.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Media-Pipeline-Stop auf Hintergrund-Thread: MediaPlayer.Pause / Playlist.Items.Clear können bei gleichzeitigen Pipeline-Events native Fehler werfen, die aber den logischen Stop (State-Reset, State-Persistenz) nicht verhindern dürfen.")]
        public Task ClearInBackground(Action beforeClear, ILogger logger)
        {
            int generation;
            lock (_gate) { generation = _generation; }

            return Task.Run(() =>
            {
                try
                {
                    lock (_gate)
                    {
                        // Startet der Nutzer unmittelbar nach dem Stopp die nächste Folge, ist dieser
                        // Schritt überholt: Er würde die neue Wiedergabe anhalten und ihre Liste leeren.
                        if (generation != _generation)
                        {
                            return;
                        }

                        beforeClear();
                        List.Items.Clear();
                        DisposeSources(Interlocked.Exchange(ref _sources, []));
                    }
                }
                catch (Exception ex)
                {
                    // Media-Pipeline-Fehler beim Stoppen sind nicht kritisch,
                    // aber für Diagnose bei subtilen Wiedergabe-Bugs hilfreich.
                    logger.Warning("Media-Pipeline-Fehler beim Stop: {Reason}", ex.Message);
                }
            });
        }

        /// <summary>Schließt die verbliebenen Quellen.</summary>
        public void Dispose() => DisposeSources(Interlocked.Exchange(ref _sources, []));

        private static void DisposeSources(List<MediaSource> sources)
        {
            foreach (MediaSource source in sources)
            {
                source.Dispose();
            }
        }
    }
}
