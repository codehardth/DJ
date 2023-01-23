using Codehard.DJ.Providers.Models;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Codehard.DJ.Providers;

public class SpotifyProvider : IMusicProvider
{
    private readonly SpotifyClient _client;
    private readonly ILogger<SpotifyProvider> _logger;
    private readonly Timer _timer;
    private readonly Queue<Music> _queue;
    private readonly Stack<Music> _playedStack;

    private Music? _currentMusic;
    private bool _disposed;

    public SpotifyProvider(
        SpotifyClient client,
        ILogger<SpotifyProvider> logger)
    {
        this._client = client;
        this._logger = logger;

        // Native queue on Spotify is lacking some basic feature
        // like queue-clearing
        // so we doing the queue in memory
        this._queue = new Queue<Music>();
        this._playedStack = new Stack<Music>();
        this._timer = new Timer(GetPlayingTrackInfoAsync, default, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public event PlayStartEventHandler? PlayStartEvent;

    public event PlayEndEventHandler? PlayEndEvent;

    public async ValueTask<IEnumerable<Music>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var searchResponse = await this._client.Search.Item(new SearchRequest(SearchRequest.Types.All, query), cancellationToken);

        return searchResponse.Tracks.Items?
                   .Select(item => new Music(
                       item.Id,
                       item.Name,
                       item.Artists.Select(a => a.Name).ToArray(),
                       item.Album.Name,
                       new Uri(item.Uri)))
               ?? Enumerable.Empty<Music>();
    }

    public ValueTask<IEnumerable<Music>> GetCurrentQueueAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(this._queue.AsEnumerable());
    }

    public ValueTask EnqueueAsync(Music music, CancellationToken cancellationToken = default)
    {
        this._queue.Enqueue(music);

        return ValueTask.CompletedTask;
    }

    public ValueTask ClearQueueAsync(CancellationToken cancellationToken = default)
    {
        this._queue.Clear();

        return ValueTask.CompletedTask;
    }

    public async ValueTask PlayAsync(Music music, CancellationToken cancellationToken = default)
    {
        var deviceResponse = await this._client.Player.GetAvailableDevices(cancellationToken);

        if (!deviceResponse.Devices.Any())
        {
            this._logger.LogError("Unable to find any device to play");

            return;
        }

        var device = deviceResponse.Devices.FirstOrDefault(d => d.IsActive)
                     ?? deviceResponse.Devices.First();

        this._logger.LogInformation("Playing on {DeviceId}-{DeviceName}", device.Id, device.Name);

        await this._client.Player.ResumePlayback(
            new PlayerResumePlaybackRequest
            {
                Uris = new[] { music.PlaySourceUri!.AbsoluteUri },
                DeviceId = device.Id,
            }, cancellationToken);

        if (this._currentMusic != null)
        {
            this.PlayEndEvent?.Invoke(this, new MusicPlayerEventArgs
            {
                Music = this._currentMusic!,
            });

            this._playedStack.Push(this._currentMusic);
        }

        this._currentMusic = music;

        this.PlayStartEvent?.Invoke(this, new MusicPlayerEventArgs
        {
            Music = music,
        });
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await this._client.Player.PausePlayback(cancellationToken);
    }

    public ValueTask<Music> StopAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public async ValueTask<Music?> NextAsync(CancellationToken cancellationToken = default)
    {
        if (!this._queue.Any())
        {
            return default;
        }

        var next = this._queue.Dequeue();

        await this.PlayAsync(next, cancellationToken);

        return next;
    }

    private async void GetPlayingTrackInfoAsync(object? state)
    {
        try
        {
            var playingContext = await this._client.Player.GetCurrentPlayback();

            if (playingContext == null!)
            {
                if (this._queue.Any())
                {
                    await this.NextAsync();
                }

                return;
            }

            if (playingContext.Item is not FullTrack currentTrack)
            {
                return;
            }

            var trackEnded = playingContext.ProgressMs >= currentTrack.DurationMs;
            var trackStopped = !playingContext.IsPlaying && playingContext.ProgressMs == 0 && this._queue.Any();

            if (trackEnded || trackStopped)
            {
                await this.NextAsync();
            }
        }
        catch (APIException ex)
        {
            this._logger.LogError(ex, "An error occurred during track info gathering");
        }
    }

    protected void Dispose(bool disposing)
    {
        if (this._disposed || !disposing)
        {
            return;
        }

        this._timer.Dispose();

        this._disposed = true;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}