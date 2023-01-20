namespace Codehard.DJ.Providers.Models;

public sealed record Music
{
    public Music(string id, string title, string[] artists, string album, Uri? playSourceUri)
    {
        this.Id = id;
        this.Title = title;
        this.Artists = artists;
        this.Album = album;
        this.PlaySourceUri = playSourceUri;
    }

    public string Id { get; }
    public string Title { get; }
    public string[] Artists { get; }
    public string Album { get; }
    public Uri? PlaySourceUri { get; }

    public void Deconstruct(out string Id, out string Title, out string[] Artists, out string Album, out Uri? PlaySourceUri)
    {
        Id = this.Id;
        Title = this.Title;
        Artists = this.Artists;
        Album = this.Album;
        PlaySourceUri = this.PlaySourceUri;
    }

    public override string ToString()
    {
        return $"🎵 {this.Title} - {this.Album} by {string.Join(",", this.Artists)}";
    }
}