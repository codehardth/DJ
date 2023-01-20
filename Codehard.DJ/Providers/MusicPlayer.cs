using Codehard.DJ.Providers.Models;

namespace Codehard.DJ.Providers;

public sealed class MusicPlayer
{
    private readonly IMusicProvider _provider;

    public MusicPlayer(IMusicProvider provider)
    {
        _provider = provider;
    }

    public ValueTask<IEnumerable<Music>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return this._provider.SearchAsync(query, cancellationToken);
    }

    public ValueTask QueueAsync(Music music, CancellationToken cancellationToken = default)
    {
        return this._provider.EnqueueAsync(music, cancellationToken);
    }
}