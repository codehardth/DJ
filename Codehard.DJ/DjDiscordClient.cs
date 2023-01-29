using System.Runtime.Caching;
using System.Text;
using Codehard.DJ.Providers;
using DJ.Domain.Entities;
using DJ.Domain.Interfaces;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Infrastructure.Discord;
using Microsoft.Extensions.Logging;

namespace Codehard.DJ;

public class DjDiscordClient : DiscordClientAbstract
{
    private readonly IMemberRepository _memberRepository;
    private readonly ILogger<DjDiscordClient> _logger;
    private readonly IMusicProvider _musicProvider;

    public DjDiscordClient(
        string token,
        IServiceProvider serviceProvider,
        IMemberRepository memberRepository,
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
        _memberRepository = memberRepository;
        this._logger = logger;
        this._musicProvider = musicProvider;

        this._musicProvider.PlayStartEvent += async (sender, args) =>
        {
            var name = $"{args.Music.Title} - {args.Music.Album} by {string.Join(", ", args.Music.Artists.Select(a => a.Name))}";

            await this.Client.UpdateStatusAsync(new DiscordActivity
            {
                Name = name.Length > 128 ? name[..128] : name,
                ActivityType = ActivityType.ListeningTo,
            });
        };

        this._musicProvider.PlayEndEvent += async (sender, _) =>
        {
            if (sender.RemainingInQueue == 0)
            {
                await this.Client.UpdateStatusAsync(new DiscordActivity
                {
                    Name = "the empty queue",
                    ActivityType = ActivityType.Watching,
                });
            }
        };

        this.Client.GuildDownloadCompleted += GuildDownloadedHandler;
        this.Client.GuildMemberAdded += GuildMemberAdded;
    }

    private async Task GuildMemberAdded(DiscordClient sender, GuildMemberAddEventArgs e)
    {
        var guildMember = e.Member;

        var exist = await this._memberRepository.AnyAsync(m => m.Id == guildMember.Id);

        if (!exist)
        {
            var member = Member.Create(guildMember.Id, guildMember.Guild.Id);

            await this._memberRepository.AddAsync(member);
            await this._memberRepository.SaveChangesAsync();
        }
    }

    private async Task GuildDownloadedHandler(DiscordClient sender, GuildDownloadCompletedEventArgs e)
    {
        var guilds = e.Guilds.Select(g => g.Value);

        var members =
            guilds.SelectMany(g =>
                g.Members.Values.Select(m => Member.Create(m.Id, g.Id)));

        await this._memberRepository.AddNewMembersAsync(members);
        await this._memberRepository.SaveChangesAsync();
    }
}

public class DjCommandHandler : BaseCommandModule
{
    private const string CacheName = "CommandCache";

    private readonly MemoryCache _cache = new(CacheName);
    private readonly IMemberRepository _memberRepository;
    private readonly IMusicProvider _musicProvider;
    private readonly ILogger<DjCommandHandler> _logger;

    public DjCommandHandler(
        IMemberRepository memberRepository,
        IMusicProvider musicProvider,
        ILogger<DjCommandHandler> logger)
    {
        _memberRepository = memberRepository;
        this._musicProvider = musicProvider;
        this._logger = logger;

        this._musicProvider.PlayEndEvent += (sender, args) =>
        {
            var key = args.Music.RandomIdentifier.ToString();

            if (this._cache.Contains(key))
                this._cache.Remove(key);
        };
    }

    [Command("skip")]
    public async Task SkipMusicAsync(CommandContext ctx)
    {
        var member = await GetMemberAsync(ctx);

        if (member == null)
        {
            await ReactAsync(ctx, Emojis.ThumbsDown);

            return;
        }

        if (!IsCurrentSongOwner(member))
        {
            await ReactAsync(ctx, Emojis.ThumbsDown);
            await ctx.RespondAsync("Please respect others rights!");

            return;
        }

        await this._musicProvider.NextAsync();

        await ReactAsync(ctx, Emojis.ThumbsUp);
    }

    [Command("q")]
    public Task QueueMusicAsync(CommandContext ctx, [RemainingText] string queryText)
    {
        return PerformWithThrottlePolicy(ctx, m => $"queue-{m.Id}", async (context, member) =>
        {
            var musics = (await this._musicProvider.SearchAsync(queryText)).ToArray();

            if (!musics.Any())
            {
                await ReactAsync(context, Emojis.ThumbsDown);

                return;
            }

            var music = musics.First();

            this._cache.Add(
                music.RandomIdentifier.ToString(),
                member.Id,
                DateTimeOffset.UtcNow.AddHours(3));

            await this._musicProvider.EnqueueAsync(music);

            await ReactAsync(context, Emojis.ThumbsUp);

            member.AddTrack(
                music.Id,
                music.Title,
                music.Artists.Select(a => a.Name),
                music.Album,
                music.PlaySourceUri,
                music.Artists.SelectMany(a => a.Genres));

            await this._memberRepository.UpdateAsync(member);
            await this._memberRepository.SaveChangesAsync();

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
                sb.AppendLine($"- {m.Title} {m.Album} {string.Join(", ", m.Artists.Select(a => a.Name))}");
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = $"The last {prevs.Length} music(s) in queue are",
                Description = sb.ToString(),
                Color = new Optional<DiscordColor>(DiscordColor.Blue),
            };

            await ctx.RespondAsync(embed);
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
            sb.AppendLine($"{music.Title} {music.Album} {string.Join(", ", music.Artists.Select(a => a.Name))}");
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
        Func<Member, string> keyFunc,
        Func<CommandContext, Member, Task> func)
    {
        var member = await GetMemberAsync(context);

        if (member == null)
        {
            await ReactAsync(context, Emojis.ThumbsDown);

            return;
        }

        var key = keyFunc(member);

        if (this.TryGetCache(key, out DateTimeOffset expirationDateTimeOffset))
        {
            await ReactAsync(context, Emojis.NoEntry);

            await context.RespondAsync(
                $"You're being throttled, " +
                $"please try again in {(int)expirationDateTimeOffset.Subtract(DateTimeOffset.UtcNow).TotalSeconds} second(s).");

            return;
        }

        var expireTime = DateTimeOffset.UtcNow.AddSeconds(3);

        await func(context, member);

        this._cache.Add(key, expireTime, new CacheItemPolicy
        {
            AbsoluteExpiration = expireTime,
        });
    }

    private bool IsCurrentSongOwner(Member member)
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

    private async Task<Member?> GetMemberAsync(CommandContext context)
    {
        var discordMember = context.Member;

        if (discordMember == null)
        {
            return null;
        }

        var member = await this._memberRepository.GetByIdAsync(discordMember.Id);

        return member;
    }
}