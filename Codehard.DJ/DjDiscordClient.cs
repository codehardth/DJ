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
            await this.Client.UpdateStatusAsync(new DiscordActivity
            {
                Name = $"{args.Music.Title} - {args.Music.Album} by {string.Join(", ", args.Music.Artists)}",
                ActivityType = ActivityType.ListeningTo,
            });
        };
    }
}

public class DjCommandHandler : BaseCommandModule
{
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
    public async Task SkipMusicAsync(CommandContext ctx)
    {
        await this._musicProvider.NextAsync();

        await ReactAsync(ctx, Emojis.ThumbsUp);
    }

    [Command("q")]
    public async Task QueueMusicAsync(CommandContext ctx, [RemainingText] string queryText)
    {
        var music = (await this._musicProvider.SearchAsync(queryText)).ToArray();

        if (music.Any())
        {
            await this._musicProvider.EnqueueAsync(music.First());

            await ReactAsync(ctx, Emojis.ThumbsUp);
        }
        else
        {
            await ReactAsync(ctx, Emojis.ThumbsDown);
        }
    }

    [Command("list-q")]
    public async Task ListQueueAsync(CommandContext ctx)
    {
        await ReactAsync(ctx, Emojis.ThumbsUp);

        var queue = (await this._musicProvider.GetCurrentQueueAsync()).ToArray();

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
            Title = "Current music(s) in queue",
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

    private static Task ReactAsync(CommandContext ctx, string emojiName)
    {
        return ctx.Message.CreateReactionAsync(DiscordEmoji.FromName(ctx.Client, emojiName));
    }
}