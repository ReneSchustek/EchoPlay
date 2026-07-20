using EchoPlay.Core.Models;
using EchoPlay.LocalLibrary.Matching;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ITrackMatcher"/>.
    /// Gibt ein fest konfiguriertes <see cref="TrackMatchKind"/> zurück.
    /// </summary>
    internal sealed class FakeTrackMatcher : ITrackMatcher
    {
        private readonly TrackMatchKind _result;

        /// <summary>
        /// Erstellt den Fake mit einem festen Klassifikationsergebnis.
        /// </summary>
        /// <param name="result">Die immer zurückzugebende Klassifikation.</param>
        public FakeTrackMatcher(TrackMatchKind result = TrackMatchKind.TbT)
        {
            _result = result;
        }

        /// <inheritdoc/>
        public TrackMatchKind Classify(int localTrackCount, int onlineTrackCount)
        {
            return _result;
        }
    }
}
