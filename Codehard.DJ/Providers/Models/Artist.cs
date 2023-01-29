namespace Codehard.DJ.Providers.Models;

public sealed record Artist(
    string Id,
    string Name,
    IReadOnlyCollection<string> Genres);