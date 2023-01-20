using Codehard.DJ.Providers.Models;

namespace Codehard.DJ.Providers;

public sealed class MusicPlayerEventArgs : EventArgs
{
    public Music Music { get; init; }
}

public delegate void PlayStartEventHandler(object sender, MusicPlayerEventArgs args);

public delegate void PlayEndEventHandler(object sender, MusicPlayerEventArgs args);

public interface IMusicProvider : IDisposable
{
    event PlayStartEventHandler PlayStartEvent;

    event PlayEndEventHandler PlayEndEvent;

    ValueTask<IEnumerable<Music>> SearchAsync(string query, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<Music>> GetCurrentQueueAsync(CancellationToken cancellationToken = default);

    ValueTask EnqueueAsync(Music music, CancellationToken cancellationToken = default);

    ValueTask ClearQueueAsync(CancellationToken cancellationToken = default);

    ValueTask PlayAsync(Music music, CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask<Music> StopAsync(CancellationToken cancellationToken = default);

    ValueTask<Music?> NextAsync(CancellationToken cancellationToken = default);
}