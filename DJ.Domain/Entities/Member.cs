namespace DJ.Domain.Entities;

public partial class Member
{
    private readonly List<PlayedTrack> _playedTracks = new();

    public Member()
    {
        // Entity Framework Constructor
    }

    private Member(
        ulong id,
        ulong guildId,
        DateTimeOffset createdAt)
    {
        this.Id = id;
        this.GuildId = guildId;
        this.CreatedAt = createdAt;
    }

    public ulong Id { get; }

    public ulong GuildId { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<PlayedTrack> PlayedTracks => this._playedTracks;

    public void AddTrack(
        string trackId,
        string title,
        IEnumerable<string> artists,
        string album,
        Uri? uri,
        bool isInappropriate,
        IEnumerable<string> genres)
    {
        var playedTrack =
            PlayedTrack.Create(
                trackId,
                title,
                artists.Distinct().ToArray(),
                album,
                genres.Distinct().ToArray(),
                isInappropriate,
                uri);

        this._playedTracks.Add(playedTrack);
    }

    public static Member Create(
        ulong id,
        ulong guildId)
        => new(id, guildId, DateTimeOffset.UtcNow);
}