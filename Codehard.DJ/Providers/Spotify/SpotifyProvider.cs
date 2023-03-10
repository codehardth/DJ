using System.Reactive.Linq;
using Codehard.DJ.Extensions;
using Codehard.DJ.Providers.Models;
using Codehard.Functional;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace Codehard.DJ.Providers.Spotify;

public class SpotifyProvider : IMusicProvider
{
    private readonly SpotifyClient _client;
    private readonly ILogger<SpotifyProvider> _logger;
    private readonly Queue<Music> _queue;
    private readonly Stack<Music> _playedStack;

    private Music? _currentMusic;
    private bool _disposed;
    private int _volume = -1;
    private readonly Action _disposer;

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

        var disposeSubscriptionAction = GetPlayingTrackInfo();

        this._disposer = disposeSubscriptionAction;
    }

    public event PlayStartEventHandler? PlayStartEvent;

    public event PlayEndEventHandler? PlayEndEvent;

    public event PlayerStateChangedEventHandler? PlayerStateChangedEvent;

    public event PlaybackOutOfSyncEventHandler? PlaybackOutOfSyncEvent;

    public Music? Current => this._currentMusic;

    public int RemainingInQueue => this._queue.Count;

    public PlaybackState State { get; private set; }

    public async ValueTask<IEnumerable<Music>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var searchResponse =
            await this._client.Search.Item(new SearchRequest(SearchRequest.Types.All, query) { Limit = limit, },
                cancellationToken);

        var artistIds = searchResponse.Tracks.Items!.SelectMany(t => t.Artists.Select(a => a.Id));

        var artistsResponse =
            await this._client.Artists.GetSeveral(new ArtistsRequest(artistIds.ToList()), cancellationToken);

        var res = searchResponse.Tracks.Items?
                      .Select(item => new Music(
                          item.Id,
                          item.Name,
                          item.Artists
                              .Join(
                                  artistsResponse.Artists.DistinctBy(a => a.Id),
                                  l => l.Id,
                                  r => r.Id,
                                  (_, r) => r)
                              .Select(fa => new Artist(fa.Id, fa.Name, fa.Genres))
                              .ToArray(),
                          new Album(item.Album.Name, item.Album.Images.Select(i => i.Url).ToArray(), string.Empty),
                          item.DurationMs,
                          new Uri(item.Uri)))
                  ?? Enumerable.Empty<Music>();

        return res;
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

        try
        {
            await this._client.Player.ResumePlayback(
                new PlayerResumePlaybackRequest
                {
                    Uris = new[] { music.PlaySourceUri!.AbsoluteUri },
                    DeviceId = device.Id,
                }, cancellationToken);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
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

    public async ValueTask<Music?> StopAsync(CancellationToken cancellationToken = default)
    {
        var pos = this._currentMusic?.Duration ?? int.MaxValue;

        await this._client.Player.PausePlayback(cancellationToken);
        await this._client.Player.SeekTo(new PlayerSeekToRequest(pos), cancellationToken);

        return this._currentMusic;
    }

    public async ValueTask<Music?> NextAsync(CancellationToken cancellationToken = default)
    {
        if (!this._queue.Any())
        {
            await this._client.Player.SkipNext(cancellationToken);

            return default;
        }

        var next = this._queue.Dequeue();

        await this.PlayAsync(next, cancellationToken);

        return next;
    }

    public async ValueTask AutoPlayAsync(CancellationToken cancellationToken = default)
    {
        const string discoverWeeklySearch = "Discover Weekly";

        var workflow =
            from response in
                Aff(async () => await this._client.Search.Item(
                    new SearchRequest(SearchRequest.Types.Playlist, discoverWeeklySearch), cancellationToken))
            let responsePlaylists = response.Playlists
            from playlist in
                responsePlaylists.Items.SingleOrNoneOrFailEff(
                        p => p.Owner.DisplayName == "Spotify" && p.Name == discoverWeeklySearch)
                    .Bind(opt =>
                        opt.Match(SuccessEff,
                            () => FailEff<SimplePlaylist>(Error.New(0, "No item available to play"))))
            from playlistTracks in
                this._client.Playlists.GetItems(playlist.Id, cancellationToken)
                    .ToAff()
                    .Map(p =>
                        p.Items.Filter(t => t.Track is FullTrack)
                            .Map(t => (FullTrack)t.Track))
            from deviceResponse in
                this._client.Player.GetAvailableDevices(cancellationToken)
                    .ToAff()
            from deviceAvailableGuard in
                guard(deviceResponse.Devices.Any(), Error.New(0, "Unable to find any device to play"))
            let device = deviceResponse.Devices.FirstOrDefault(d => d.IsActive)
                         ?? deviceResponse.Devices.First()
            let first = playlistTracks.Shuffle().First()
            from playFirst in
                this._client.Player.ResumePlayback(new PlayerResumePlaybackRequest
                    {
                        Uris = new[] { first.Uri },
                        DeviceId = device.Id,
                    }, cancellationToken)
                    .ToAff()
            select unit;

        _ = await workflow.Run();
    }

    public async ValueTask<bool> MuteAsync(CancellationToken cancellationToken = default)
    {
        var oldVolume = this._volume;

        try
        {
            this._volume = this._volume == -1 ? 100 : this._volume;

            var result = await this._client.Player.SetVolume(new PlayerVolumeRequest(0), cancellationToken);

            return result;
        }
        catch
        {
            this._volume = oldVolume;

            return false;
        }
    }

    public async ValueTask<bool> UnmuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await this._client.Player.SetVolume(new PlayerVolumeRequest(this._volume), cancellationToken);

            return result;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<bool> SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        var oldVolume = this._volume;

        try
        {
            this._volume = Math.Clamp(volume, 0, 100);

            var result = await this._client.Player.SetVolume(new PlayerVolumeRequest(this._volume), cancellationToken);

            return result;
        }
        catch
        {
            this._volume = oldVolume;

            return false;
        }
    }

    private Action GetPlayingTrackInfo()
    {
        var playbackStateObserver =
            Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(3))
                .SelectMany(_ => Observable.FromAsync(GetPlayerInfoAsync))
                .Do(t => this.State = t.State);

        var stateChangedSubscription =
            playbackStateObserver
                .DistinctUntilChanged(t => t.State)
                .Subscribe(t => this.PlayerStateChangedEvent?.Invoke(this, t.State));

        var playStoppedOrEndedSubscription =
            playbackStateObserver
                .Where(t => t.State is PlaybackState.Ended or PlaybackState.Stopped)
                .Select(t => t.TrackAudio)
                .Subscribe(PlayerStoppedOrEndedSubscribeHandler);

        var playbackOutOfSyncSubscription =
            playbackStateObserver
                .Where(t =>
                    t.State is PlaybackState.Playing &&
                    t.TrackAudio!.Id != this._currentMusic?.Id)
                .Select(t => t.TrackAudio)
                .DistinctUntilChanged(_ => this.RemainingInQueue)
                .Subscribe(PlaybackOutOfSyncHandler!);

        return () =>
        {
            stateChangedSubscription.Dispose();
            playStoppedOrEndedSubscription.Dispose();
            playbackOutOfSyncSubscription.Dispose();
        };

        async void PlaybackOutOfSyncHandler(FullTrack track)
        {
            TryInvokePlaybackEnded();

            this.PlaybackOutOfSyncEvent?.Invoke(this,
                new MusicPlayerEventArgs
                {
                    Music = new Music(track.Id, track.Name, track.Artists.Select(a => new Artist(a.Id, a.Name, System.Array.Empty<string>())).ToArray(),
                        new Album(track.Album.Name, track.Album.Images.Select(i => i.Url).ToArray(), string.Empty), track.DurationMs, new Uri(track.Uri)),
                });

            if (this.RemainingInQueue > 0)
            {
                await this.StopAsync();
            }
        }

        async void PlayerStoppedOrEndedSubscribeHandler(FullTrack? _)
        {
            TryInvokePlaybackEnded();

            if (this._queue.Any())
            {
                await this.NextAsync();
            }
        }

        async Task<(FullTrack? TrackAudio, PlaybackState State)> GetPlayerInfoAsync()
        {
            try
            {
                var playingContext = await this._client.Player.GetCurrentPlayback();

                if (playingContext == null!)
                {
                    return (default, PlaybackState.Stopped);
                }

                if (playingContext.Item is not FullTrack currentTrack)
                {
                    return (default, PlaybackState.Stopped);
                }

                var trackEnded = playingContext.ProgressMs >= currentTrack.DurationMs - 1;
                var trackStopped = playingContext is { IsPlaying: false, ProgressMs: 0 };

                var playbackState =
                    trackEnded ? PlaybackState.Ended :
                    trackStopped ? PlaybackState.Stopped :
                    PlaybackState.Playing;

                return (currentTrack, playbackState);
            }
            catch
            {
                return (null, PlaybackState.Unknown);
            }
        }

        void TryInvokePlaybackEnded()
        {
            if (this._currentMusic != null)
            {
                this._playedStack.Push(this._currentMusic);

                this.PlayEndEvent?.Invoke(this, new MusicPlayerEventArgs
                {
                    Music = this._currentMusic!,
                });

                this._currentMusic = null;
            }
        }
    }

    protected void Dispose(bool disposing)
    {
        if (this._disposed || !disposing)
        {
            return;
        }

        this._disposer();

        this._disposed = true;
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }
}