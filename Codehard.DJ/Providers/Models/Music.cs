namespace Codehard.DJ.Providers.Models;

public sealed record Music
{
    public Music(
        string id,
        string title,
        Artist[] artists,
        Album album,
        int duration,
        Uri? playSourceUri)
    {
        this.Id = id;
        this.Title = title;
        this.Artists = artists;
        this.Album = album;
        this.PlaySourceUri = playSourceUri;
        this.RandomIdentifier = Guid.NewGuid();
    }

    public string Id { get; }

    public string Title { get; }

    public Artist[] Artists { get; }

    public Album Album { get; }

    public int Duration { get; }

    public Uri? PlaySourceUri { get; }

    public Guid RandomIdentifier { get; }

    public override string ToString()
    {
        return $"🎵 {this.Title} - {this.Album.Name} by {string.Join(", ", this.Artists.Select(a => a.Name))}";
    }
}