using System.Runtime.Caching;
using System.Text;
using System.Threading.Channels;
using Codehard.DJ.Providers;
using DJ.Domain.Entities;
using DJ.Domain.Interfaces;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Infrastructure.Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Codehard.DJ;

file sealed record Message(DiscordClient Client, DiscordChannel Channel, DiscordUser User, string Content);

file static class SharedChannel<T>
{
    public static readonly ChannelReader<T> Reader;

    public static readonly ChannelWriter<T> Writer;

    static SharedChannel()
    {
        var channel = Channel.CreateBounded<T>(100);

        Reader = channel.Reader;
        Writer = channel.Writer;
    }
}

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
            true,
            true,
            serviceProvider)
    {
        this._memberRepository = memberRepository;
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
        this.Client.MessageReactionAdded += MessageReactionAddedHandler;
    }

    private async Task MessageReactionAddedHandler(DiscordClient sender, MessageReactionAddEventArgs e)
    {
        if (e.Message.Author.Id != sender.CurrentUser.Id)
        {
            return;
        }

        var message = await e.Channel.GetMessageAsync(e.Message.Id);

        if (!message.Embeds.Any())
        {
            return;
        }

        var query = message.Embeds[0].Footer?.Text;

        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        await SharedChannel<Message>.Writer.WriteAsync(new Message(sender, e.Channel, e.User, query));
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

public partial class DjCommandHandler : BaseCommandModule
{
    private const string CacheName = "CommandCache";

    private readonly MemoryCache _cache = new(CacheName);
    private readonly IMemberRepository _memberRepository;
    private readonly IMusicProvider _musicProvider;
    private readonly ILogger<DjCommandHandler> _logger;
    private readonly Timer channelReaderTimer;
    private readonly int _cooldown;

    public DjCommandHandler(
        IMemberRepository memberRepository,
        IMusicProvider musicProvider,
        IConfiguration configuration,
        ILogger<DjCommandHandler> logger)
    {
        this._memberRepository = memberRepository;
        this._musicProvider = musicProvider;
        this._logger = logger;
        this._cooldown =
            configuration.GetSection("Configurations")
                .GetSection("Discord")
                .GetValue<int>("CommandCooldown");

        this._musicProvider.PlayEndEvent += (sender, args) =>
        {
            var key = args.Music.RandomIdentifier.ToString();

            if (this._cache.Contains(key))
                this._cache.Remove(key);
        };

        Task.Run(ReadChannelAsync);
    }

    private async Task ReadChannelAsync()
    {
        while (await SharedChannel<Message>.Reader.WaitToReadAsync())
        {
            while (SharedChannel<Message>.Reader.TryRead(out var message))
            {
                await QueueMusicAsync(message.Client, message.Channel, message.User, message.Content);
            }
        }
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

        if (this._musicProvider.Current == null)
        {
            await ReactAsync(ctx, Emojis.NoEntry);
            await ctx.RespondAsync("Current track is not tracking by the bot.");

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
            var musics = (await this._musicProvider.SearchAsync(queryText, 1)).ToArray();

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

    public Task QueueMusicAsync(
        DiscordClient client,
        DiscordChannel channel,
        DiscordUser discordUser,
        string queryText)
    {
        return PerformWithThrottlePolicy(
            client,
            channel,
            discordUser,
            m => $"queue-{m.Id}", async (_, _, _, member) =>
            {
                var musics = (await this._musicProvider.SearchAsync(queryText)).ToArray();

                if (!musics.Any())
                {
                    await ReactAsync(client, channel, discordUser, Emojis.ThumbsDown);

                    return;
                }

                var music = musics.First();

                this._cache.Add(
                    music.RandomIdentifier.ToString(),
                    member.Id,
                    DateTimeOffset.UtcNow.AddHours(3));

                await this._musicProvider.EnqueueAsync(music);
                await ReactAsync(client, channel, discordUser, Emojis.ThumbsUp);

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
                    await client.SendMessageAsync(channel, $"{discordUser.Mention} {totalInQueue - 1} music(s) ahead in queue");
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

                await client.SendMessageAsync(channel, embed);
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
        var searchResult = (await this._musicProvider.SearchAsync(queryText, 3)).ToArray();

        if (!searchResult.Any())
        {
            await ctx.RespondAsync("There is music matching your search text.");

            return;
        }

        foreach (var music in searchResult)
        {
            var artists = string.Join(", ", music.Artists.Select(a => a.Name));

            var embed = new DiscordEmbedBuilder
            {
                Title = $"{queryText} from {ctx.User.Username}",
                Description = $"🎵 {music.Title}\n🧑‍🎤 {artists}\n💿 {music.Album}",
                Footer = new DiscordEmbedBuilder.EmbedFooter()
                {
                    Text = $"{music.Title} {music.Album} {artists}",
                },
                Color = new Optional<DiscordColor>(DiscordColor.Blue),
            };

            await ctx.RespondAsync(embed);
        }
    }

    [Command("stat")]
    public async Task GetStatAsync(CommandContext ctx)
    {
        var member = await GetMemberAsync(ctx);

        if (member == null)
        {
            await ReactAsync(ctx, Emojis.ThumbsDown);

            return;
        }

        var playedTracks = member.PlayedTracks;

        if (!playedTracks.Any())
        {
            await ctx.RespondAsync("There is no history so far, try queueing one!");

            return;
        }

        var mostPlayedGenre =
            playedTracks.SelectMany(t => t.Genres).GroupBy(g => g).MaxBy(g => g.Count())?.Key;
        var mostPlayedArtist =
            playedTracks.SelectMany(t => t.Artists).GroupBy(a => a).MaxBy(g => g.Count())?.Key;
        var totalPlayedTracks = playedTracks.Count;
        var totalPlayedDays =
            playedTracks.GroupBy(t => new { t.CreatedAt.Day, t.CreatedAt.Month, t.CreatedAt.Year })
                .Select(g => g.Key)
                .Count();
        var averagePlayedPerDay = playedTracks.Count / totalPlayedDays;

        var sb = new StringBuilder();
        sb.AppendLine($"🎵 Most played genre: {mostPlayedGenre ?? "Unknown"}");
        sb.AppendLine($"🎵 Most played artist: {mostPlayedArtist ?? "Unknown"}");
        sb.AppendLine($"🎵 Total requested tracks: {totalPlayedTracks}");
        sb.AppendLine($"🎵 Average requested track per day: {averagePlayedPerDay}");

        var embed = new DiscordEmbedBuilder
        {
            Title = "Here's some insight for you",
            Description = sb.ToString(),
            Color = new Optional<DiscordColor>(DiscordColor.Purple),
        };

        await ctx.RespondAsync(embed);
    }
}