namespace Codehard.DJ.Providers.Models;

public sealed record Music
{
    public Music(
        string id,
        string title,
        string[] artists,
        string album,
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

    public string[] Artists { get; }

    public string Album { get; }

    public Uri? PlaySourceUri { get; }

    public Guid RandomIdentifier { get; }

    public override string ToString()
    {
        return $"🎵 {this.Title} - {this.Album} by {string.Join(", ", this.Artists)}";
    }
}