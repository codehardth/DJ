using System.Runtime.Caching;
using System.Text;
using Codehard.DJ.Providers;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Infrastructure.Discord;
using Microsoft.Extensions.Logging;

namespace Codehard.DJ;

public class DjDiscordClient : DiscordClientAbstract
{
    private readonly ILogger<DjDiscordClient> _logger;
    private readonly IMusicProvider _musicProvider;

    public DjDiscordClient(
        string token,
        IServiceProvider serviceProvider,
        ILogger<DjDiscordClient> logger,
        IMusicProvider musicProvider)
        : base(
            token,
            new[] { "!" },
            new[] { typeof(DjCommandHandler) },
            false,
            true,
            serviceProvider)
    {
        this._logger = logger;
        this._musicProvider = musicProvider;

        this._musicProvider.PlayStartEvent += async (sender, args) =>
        {
            var name = $"{args.Music.Title} - {args.Music.Album} by {string.Join(", ", args.Music.Artists)}";

            await this.Client.UpdateStatusAsync(new DiscordActivity
            {
                Name = name.Length > 128 ? name[..128] : name,
                ActivityType = ActivityType.ListeningTo,
            });
        };

        this._musicProvider.PlayEndEvent += async (_, _) =>
        {
            if (this._musicProvider.RemainingInQueue == 0)
            {
                await this.Client.UpdateStatusAsync(new DiscordActivity
                {
                    Name = "Sleeping...",
                    ActivityType = ActivityType.Custom,
                });
            }
        };
    }
}

public class DjCommandHandler : BaseCommandModule
{
    private const string CacheName = "CommandCache";

    private readonly MemoryCache _cache = new(CacheName);
    private readonly IMusicProvider _musicProvider;
    private readonly ILogger<DjCommandHandler> _logger;

    public DjCommandHandler(
        IMusicProvider musicProvider,
        ILogger<DjCommandHandler> logger)
    {
        this._musicProvider = musicProvider;
        this._logger = logger;
    }

    [Command("skip")]
    public Task SkipMusicAsync(CommandContext ctx)
    {
        return PerformWithThrottlePolicy(ctx, m => $"skip-{m.Id}", async (context, member) =>
        {
            if (!IsCurrentSongOwner(member))
            {
                await ReactAsync(context, Emojis.ThumbsDown);
                await context.RespondAsync("Please respect others rights!");

                return;
            }

            await this._musicProvider.NextAsync();

            await ReactAsync(context, Emojis.ThumbsUp);
        });
    }

    [Command("q")]
    public Task QueueMusicAsync(CommandContext ctx, [RemainingText] string queryText)
    {
        return PerformWithThrottlePolicy(ctx, m => $"queue-{m.Id}", async (context, member) =>
        {
            var musics = (await this._musicProvider.SearchAsync(queryText)).ToArray();

            if (musics.Any())
            {
                var music = musics.First();

                this._cache.Add(
                    music.RandomIdentifier.ToString(),
                    member.Id,
                    DateTimeOffset.UtcNow.AddMinutes(5));

                await this._musicProvider.EnqueueAsync(music);

                await ReactAsync(context, Emojis.ThumbsUp);

                var queue = await this._musicProvider.GetCurrentQueueAsync();

                var prevs = queue.TakeLast(3).ToArray();

                if (!prevs.Any())
                {
                    return;
                }

                var totalInQueue = this._musicProvider.RemainingInQueue;

                if (totalInQueue > 1)
                {
                    await context.RespondAsync($"{totalInQueue - 1} music(s) ahead in queue");
                }

                var sb = new StringBuilder();

                foreach (var m in prevs)
                {
                    sb.AppendLine($"- {m.Title} {m.Album} {string.Join(", ", m.Artists)}");
                }

                var embed = new DiscordEmbedBuilder
                {
                    Title = $"The last {prevs.Length} music(s) in queue are",
                    Description = sb.ToString(),
                    Color = new Optional<DiscordColor>(DiscordColor.Blue),
                };

                await ctx.RespondAsync(embed);
            }
            else
            {
                await ReactAsync(context, Emojis.ThumbsDown);
            }
        });
    }

    [Command("list-q")]
    public async Task ListQueueAsync(CommandContext ctx)
    {
        await ReactAsync(ctx, Emojis.ThumbsUp);

        var queue = (await this._musicProvider.GetCurrentQueueAsync()).Take(10).ToArray();

        if (!queue.Any())
        {
            await ctx.RespondAsync("There is no music in queue");

            return;
        }

        var sb = new StringBuilder();

        foreach (var music in queue)
        {
            sb.AppendLine(music.ToString());
        }

        var embed = new DiscordEmbedBuilder
        {
            Title = $"Current top {queue.Length} music(s) in queue",
            Description = sb.ToString(),
            Color = new Optional<DiscordColor>(DiscordColor.Green),
        };

        await ctx.RespondAsync(embed);
    }

    [Command("search")]
    public async Task SearchAsync(CommandContext ctx, [RemainingText] string queryText)
    {
        var searchResult = (await this._musicProvider.SearchAsync(queryText)).ToArray();

        if (!searchResult.Any())
        {
            await ctx.RespondAsync("There is music matching your search text.");

            return;
        }

        var sb = new StringBuilder();

        foreach (var music in searchResult)
        {
            sb.AppendLine($"{music.Title} {music.Album} {string.Join(", ", music.Artists)}");
        }

        var embed = new DiscordEmbedBuilder
        {
            Title = $"Search result for {queryText}",
            Description = sb.ToString(),
            Color = new Optional<DiscordColor>(DiscordColor.Blue),
        };

        await ctx.RespondAsync(embed);
    }

    private async Task PerformWithThrottlePolicy(
        CommandContext context,
        Func<DiscordMember, string> keyFunc,
        Func<CommandContext, DiscordMember, Task> func)
    {
        if (!TryGetMember(context, out var member))
        {
            await ReactAsync(context, Emojis.ThumbsDown);

            return;
        }

        var key = keyFunc(member);

        if (this.TryGetCache(key, out DateTimeOffset expirationDateTimeOffset))
        {
            await ReactAsync(context, Emojis.ThumbsDown);

            await context.RespondAsync(
                $"You're being throttle, " +
                $"please try again in {(int)expirationDateTimeOffset.Subtract(DateTimeOffset.UtcNow).TotalSeconds} second(s).");

            return;
        }

        var expireTime = DateTimeOffset.UtcNow.AddMinutes(3);

        await func(context, member);

        this._cache.Add(key, expireTime, new CacheItemPolicy
        {
            AbsoluteExpiration = expireTime,
        });
    }

    private bool IsCurrentSongOwner(DiscordMember member)
    {
        var current = this._musicProvider.Current;

        if (current == null || !TryGetCache(current.RandomIdentifier.ToString(), out ulong memberId))
        {
            return false;
        }

        return member.Id == memberId;
    }

    private bool TryGetCache<T>(string key, out T value)
    {
        if (!this._cache.Contains(key))
        {
            value = default!;

            return false;
        }

        value = (T)this._cache[key];

        return true;
    }

    private static Task ReactAsync(CommandContext ctx, string emojiName)
    {
        return ctx.Message.CreateReactionAsync(DiscordEmoji.FromName(ctx.Client, emojiName));
    }

    private static bool TryGetMember(CommandContext context, out DiscordMember member)
    {
        return (member = context.Member!) != null;
    }
}