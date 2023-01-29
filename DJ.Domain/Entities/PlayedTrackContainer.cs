namespace DJ.Domain.Entities;

public class PlayedTrackContainer
{
    public const string PlayedTracksBackingField = nameof(_playedTracks);
    
    private readonly List<PlayedTrack> _playedTracks = new();

    public IReadOnlyCollection<PlayedTrack> PlayedTracks => this._playedTracks;

    public void AddTrack(PlayedTrack playedTrack)
    {
        this._playedTracks.Add(playedTrack);
    }
}