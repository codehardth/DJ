namespace Codehard.DJ.Providers.Models;

public sealed record Album
{
    public Album(string Name, string[] Images, string Genre)
    {
        this.Name = Name;
        this.Images = Images;
        this.Genre = Genre;
    }

    public string Name { get; init; }

    public string[] Images { get; init; }

    public string Genre { get; init; }

    public override string ToString()
    {
        return this.Name;
    }
}