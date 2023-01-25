using Codehard.DJ.Providers.Models;
using Codehard.DJ.Providers.Spotify;

namespace Codehard.DJ.Providers;

public sealed class MusicPlayerEventArgs : EventArgs
{
    public Music Music { get; init; }
}

public delegate void PlayStartEventHandler(IMusicProvider sender, MusicPlayerEventArgs args);

public delegate void PlayEndEventHandler(IMusicProvider sender, MusicPlayerEventArgs args);

public interface IMusicProvider : IDisposable
{
    event PlayStartEventHandler PlayStartEvent;

    event PlayEndEventHandler PlayEndEvent;

    Music? Current { get; }

    int RemainingInQueue { get; }

    PlaybackState State { get; }

    ValueTask<IEnumerable<Music>> SearchAsync(string query, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<Music>> GetCurrentQueueAsync(CancellationToken cancellationToken = default);

    ValueTask EnqueueAsync(Music music, CancellationToken cancellationToken = default);

    ValueTask ClearQueueAsync(CancellationToken cancellationToken = default);

    ValueTask PlayAsync(Music music, CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask<Music> StopAsync(CancellationToken cancellationToken = default);

    ValueTask<Music?> NextAsync(CancellationToken cancellationToken = default);
}