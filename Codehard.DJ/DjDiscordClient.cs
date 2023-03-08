using System.Runtime.Caching;
using System.Text;
using System.Threading.Channels;
using Codehard.DJ.Extensions;
using Codehard.DJ.Providers;
using Codehard.DJ.Providers.Models;
using Codehard.DJ.Providers.Spotify;
using DJ.Domain.Entities;
using DJ.Domain.Interfaces;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using Infrastructure.Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

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

    private static string? latestPresence = default;

    public DjDiscordClient(
        string token,
        IServiceProvider serviceProvider,
        IMemberRepository memberRepository,
        ILogger<DjDiscordClient> logger,
        IMusicProvider musicProvider)
        : base(
            token,
            new[] { "!" },
            true,
            true,
            serviceProvider)
    {
        this._memberRepository = memberRepository;
        this._logger = logger;
        this._musicProvider = musicProvider;

        this.Client.Ready += ClientReadyHandler;
        this.Client.GuildDownloadCompleted += GuildDownloadedHandler;
        this.Client.GuildMemberAdded += GuildMemberAdded;
        this.Client.MessageReactionAdded += MessageReactionAddedHandler;
    }

    protected override void ConfigureCommandsNext(CommandsNextExtension commandsNextExtension)
    {
    }

    protected override void ConfigureSlashCommands(SlashCommandsExtension slashCommandsExtension)
    {
        slashCommandsExtension.RegisterCommands<DjCommandModule>();
    }

    private Task ClientReadyHandler(DiscordClient sender, ReadyEventArgs e)
    {
        this._musicProvider.PlayStartEvent += PlayStartEventHandler;
        this._musicProvider.PlayEndEvent += PlayEndEventHandler;
        this._musicProvider.PlayerStateChangedEvent += PlayerStateChangedHandler;
        this._musicProvider.PlaybackOutOfSyncEvent += PlaybackOutOfSyncHandler;

        return Task.CompletedTask;
    }

    private async void PlaybackOutOfSyncHandler(IMusicProvider sender, MusicPlayerEventArgs args)
    {
        var name =
            $"{args.Music.Title} - {args.Music.Album} by {string.Join(", ", args.Music.Artists.Select(a => a.Name))}";

        if (name == latestPresence)
        {
            return;
        }

        latestPresence = name;

        await this.Client.UpdateStatusAsync(new DiscordActivity
        {
            Name = name.Length > 128 ? name[..128] : name,
            ActivityType = ActivityType.ListeningTo,
        });
    }

    private async void PlayEndEventHandler(IMusicProvider sender, MusicPlayerEventArgs args)
    {
        if (sender.RemainingInQueue == 0)
        {
            this._logger.LogInformation("Music queue is now empty, auto playing...");
        }
    }

    private async void PlayStartEventHandler(IMusicProvider sender, MusicPlayerEventArgs args)
    {
        var name =
            $"{args.Music.Title} - {args.Music.Album} by {string.Join(", ", args.Music.Artists.Select(a => a.Name))}";

        await this.Client.UpdateStatusAsync(new DiscordActivity
        {
            Name = name.Length > 128 ? name[..128] : name,
            ActivityType = ActivityType.ListeningTo,
        });
    }

    private async void PlayerStateChangedHandler(IMusicProvider sender, PlaybackState state)
    {
        if (state == PlaybackState.Stopped)
        {
            latestPresence = null;

            await this.Client.UpdateStatusAsync(new DiscordActivity
            {
                Name = "an empty music queue...",
                ActivityType = ActivityType.Watching,
            });
        }
    }

    private static async Task MessageReactionAddedHandler(DiscordClient sender, MessageReactionAddEventArgs e)
    {
        if (e.Message.Author?.Id != sender.CurrentUser.Id)
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

public partial class DjCommandModule : ApplicationCommandModule
{
    private const string CacheName = "CommandCache";

    private static readonly MemoryCache _cache = new(CacheName);
    private readonly OpenAIClient _api;
    private readonly IMemberRepository _memberRepository;
    private readonly IMusicProvider _musicProvider;
    private readonly ILogger<DjCommandModule> _logger;
    private readonly Timer channelReaderTimer;
    private readonly int _cooldown;

    public DjCommandModule(
        OpenAIClient api,
        IMemberRepository memberRepository,
        IMusicProvider musicProvider,
        IConfiguration configuration,
        ILogger<DjCommandModule> logger)
    {
        _api = api;
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

            if (_cache.Contains(key))
            {
                _cache.Remove(key);
            }
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

    [SlashCommand("skip", "Skip current music.")]
    public async Task SkipMusicAsync(InteractionContext ctx)
    {
        if (this._musicProvider.Current == null && this._musicProvider.RemainingInQueue == 0)
        {
            await this._musicProvider.NextAsync();
            await ReactAsync(ctx, Emojis.ThumbsUp, true);

            return;
        }

        var member = await GetMemberAsync(ctx);

        if (member == null)
        {
            await ReactAsync(ctx, Emojis.ThumbsDown, true);

            return;
        }

        if (this._musicProvider.Current == null)
        {
            await ReactAsync(ctx, Emojis.NoEntry, true);
            await ctx.CreateResponseAsync("Current track is not tracking by the bot.", true);

            return;
        }

        if (!IsCurrentSongOwner(member))
        {
            await ReactAsync(ctx, Emojis.ThumbsDown, true);
            await ctx.CreateResponseAsync("Please respect others rights!", true);

            return;
        }

        await this._musicProvider.NextAsync();

        await ReactAsync(ctx, Emojis.ThumbsUp, true);
    }

    [SlashCommand("auto", "Auto play the music.")]
    public async Task AutoPlayAsync(InteractionContext ctx)
    {
        if (!(this._musicProvider.Current == null && this._musicProvider.RemainingInQueue == 0))
        {
            await ReactAsync(ctx, Emojis.ThumbsDown);
            await ctx.CreateResponseAsync("There are musics currently in queue!");

            return;
        }

        try
        {
            await this._musicProvider.AutoPlayAsync();

            await ReactAsync(ctx, Emojis.ThumbsUp, true);
        }
        catch (Exception ex)
        {
            await ReactAsync(ctx, Emojis.ThumbsDown, true);

            await ctx.FollowUpAsync(ex.Message, true);
        }
    }

    [SlashCommand("q", "Queue the music using query text")]
    public Task QueueMusicAsync(InteractionContext ctx,
        [Option("queryText", "Query text to search for music to place in queue."), RemainingText]
        string queryText)
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

            _cache.Add(
                music.RandomIdentifier.ToString(),
                member.Id,
                DateTimeOffset.UtcNow.AddHours(3));

            await this._musicProvider.EnqueueAsync(music);

            await ReactAsync(context, Emojis.ThumbsUp, true);

            member.AddTrack(
                music.Id,
                music.Title,
                music.Artists.Select(a => a.Name),
                music.Album.Name,
                music.PlaySourceUri,
                await this.CheckIfInappropriateAsync(music),
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
                await context.FollowUpAsync($"{totalInQueue - 1} music(s) ahead in queue", true);
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
                ImageUrl = music.Album.Images.FirstOrDefault(),
            };

            await ctx.FollowUpAsync(embed, true);
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

                _cache.Add(
                    music.RandomIdentifier.ToString(),
                    member.Id,
                    DateTimeOffset.UtcNow.AddHours(3));

                await this._musicProvider.EnqueueAsync(music);
                await ReactAsync(client, channel, discordUser, Emojis.ThumbsUp);

                member.AddTrack(
                    music.Id,
                    music.Title,
                    music.Artists.Select(a => a.Name),
                    music.Album.Name,
                    music.PlaySourceUri,
                    await this.CheckIfInappropriateAsync(music),
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
                    await client.SendMessageAsync(channel,
                        $"{discordUser.Mention} {totalInQueue - 1} music(s) ahead in queue");
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
                    ImageUrl = music.Album.Images.FirstOrDefault(),
                };

                await client.SendMessageAsync(channel, embed);
            });
    }

    private async Task<bool> CheckIfInappropriateAsync(Music music)
    {
        try
        {
            var chatRequest = new ChatRequest(new ChatPrompt[]
            {
                new("system",
                    "You are an expert in languages, and can tell if a word is considered rude or inappropriate, you always answer with true or false only and nothing else."),
                new("user", "Is the word bitch contains some word considered rude or inappropriate?"),
                new("assistant", "true"),
                new("user", $"Is {music} contains some word considered rude or inappropriate? You may only answer with true or false only."),
            });

            var result = await this._api.ChatEndpoint.GetCompletionAsync(chatRequest);

            return bool.TryParse(result.FirstChoice, out var res) && res;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    [SlashCommand("list-q", "Display the music in queue.")]
    public async Task ListQueueAsync(InteractionContext ctx)
    {
        await ReactAsync(ctx, Emojis.ThumbsUp, true);

        var queue = (await this._musicProvider.GetCurrentQueueAsync()).Take(10).ToArray();

        if (!queue.Any())
        {
            await ctx.FollowUpAsync("There is no music in queue", true);

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

        await ctx.FollowUpAsync(embed, true);
    }

    [SlashCommand("search", "Search for the music.")]
    public async Task SearchAsync(
        InteractionContext ctx,
        [Option("queryText", "Query text to search for music.")]
        string queryText,
        [Option("size", "Search result size.", true)]
        long size = 3)
    {
        var searchResult = (await this._musicProvider.SearchAsync(queryText, (int)Math.Clamp(size, 1, 10))).ToArray();

        if (!searchResult.Any())
        {
            await ctx.CreateResponseAsync("There is music matching your search text.");

            return;
        }

        var embeds =
            searchResult.Map(music =>
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
                    ImageUrl = music.Album.Images.FirstOrDefault(),
                };

                return embed.Build();
            });

        await ctx.CreateResponseAsync(embeds);
    }

    [SlashCommand("stat", "Display listening statistics.")]
    public async Task GetStatAsync(InteractionContext ctx)
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
            await ctx.CreateResponseAsync("There is no history so far, try queueing one!");

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

        await ctx.CreateResponseAsync(embed, true);
    }

    [SlashCommand("mute", "Mute the music.")]
    public async Task MuteAsync(InteractionContext ctx)
    {
        var success = await this._musicProvider.MuteAsync();

        await ReactAsync(ctx, success ? Emojis.ThumbsUp : Emojis.ThumbsDown, true);
    }

    [SlashCommand("unmute", "Unmute the bot.")]
    public async Task UnmuteAsync(InteractionContext ctx)
    {
        var success = await this._musicProvider.UnmuteAsync();

        await ReactAsync(ctx, success ? Emojis.ThumbsUp : Emojis.ThumbsDown, true);
    }

    [SlashCommand("vol", "Set volume for the bot.")]
    public async Task UnmuteAsync(InteractionContext ctx, [Option("volume", "Volume value.")] long volume)
    {
        var success = await this._musicProvider.SetVolumeAsync((int)volume);

        await ReactAsync(ctx, success ? Emojis.ThumbsUp : Emojis.ThumbsDown, true);
    }

    [SlashCommand("ban", "Ban a user from accessing bot.")]
    [RequireUserPermissions(Permissions.Administrator)]
    public async Task BanUserAsync(InteractionContext ctx,
        [Option("mentionText", "A user mention text.")]
        string mentionText,
        [Option("banMinute", "Ban duration in minute.")]
        long banMinute = 10)
    {
        var banExpireTime = DateTimeOffset.UtcNow.AddMinutes(banMinute);

        if (!TryGetUserId(mentionText, out var id))
        {
            await ReactAsync(ctx, Emojis.ThumbsDown, true);

            return;
        }

        var key = GetBanKey(id);

        _cache.Add(key, banExpireTime, new CacheItemPolicy
        {
            AbsoluteExpiration = banExpireTime,
        });

        await ctx.CreateResponseAsync($"{mentionText} has been banned for {banMinute} minutes from queue command.");
    }

    [SlashCommand("unban", "Unban.")]
    [RequireUserPermissions(Permissions.Administrator)]
    public async Task UnbanUserAsync(InteractionContext ctx,
        [Option("mentionText", "A user mention text.")]
        string mentionText)
    {
        if (!TryGetUserId(mentionText, out var id))
        {
            await ReactAsync(ctx, Emojis.ThumbsDown, true);

            return;
        }

        var key = GetBanKey(id);

        _cache.Remove(key);

        await ctx.CreateResponseAsync($"{mentionText} ban has been lifted.");
    }

    [SlashCommand("chat", "Chat with the 'music expert'")]
    public async Task ChatAsync(
        InteractionContext ctx,
        [Option("query", "A query text")] string query)
    {
        await ctx.DeferAsync();

        try
        {
            var chatRequest = new ChatRequest(new ChatPrompt[]
            {
                new("system", "You are a music expert and able to answer any question related to music."),
                new ("system", "When user asking some question without a context given, you usually answer with a recommended music in format of  Music_Name from Album_Name of Artist_Name"),
                new("user", "What's a good song about the hammer?"),
                new("assistant", "Maxwell's Silver Hammer from Abbey Road by The Beatles"),
                new("user", query),
            });

            var result = await this._api.ChatEndpoint.GetCompletionAsync(chatRequest);

            await ctx.EditResponseAsync(new DiscordWebhookBuilder
            {
                Content = result.FirstChoice,
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error!");

            await ctx.EditResponseAsync(new DiscordWebhookBuilder
            {
                Content = "I can't answer that.",
            });
        }
    }
}