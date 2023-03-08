namespace DJ.Domain.Entities;

public class PlayedTrack
{
    public PlayedTrack(bool considerInappropriate)
    {
        ConsiderInappropriate = considerInappropriate;
        // Entity Framework Constructor
    }

    private PlayedTrack(
        Guid id,
        string trackId,
        string title,
        string[] artists,
        string album,
        string[] genres,
        int score,
        Uri? uri,
        bool considerInappropriate,
        DateTimeOffset createdAt)
    {
        this.Id = id;
        this.TrackId = trackId;
        this.Title = title;
        this.Artists = artists;
        this.Album = album;
        this.Genres = genres;
        this.Score = score;
        this.Uri = uri;
        this.ConsiderInappropriate = considerInappropriate;
        this.CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public string TrackId { get; }

    public string Title { get; }

    public string[] Artists { get; }

    public string Album { get; }

    public string[] Genres { get; }

    public int Score { get; }

    public Uri? Uri { get; }

    public bool ConsiderInappropriate { get; }

    public DateTimeOffset CreatedAt { get; }

    public virtual Member Member { get; protected set; }

    internal static PlayedTrack Create(
        string trackId,
        string title,
        string[] artists,
        string album,
        string[] genres,
        bool isInappropriate,
        Uri? uri)
        => new(Guid.Empty,
            trackId,
            title,
            artists,
            album,
            genres,
            5,
            uri,
            isInappropriate,
            DateTimeOffset.UtcNow);
}