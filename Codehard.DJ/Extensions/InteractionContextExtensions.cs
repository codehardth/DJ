using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace Codehard.DJ.Extensions;

public static class InteractionContextExtensions
{
    public static async Task CreateResponseAsync(this InteractionContext context, IEnumerable<DiscordEmbed> embeds, bool ephemeral = false, CancellationToken cancellationToken = default)
    {
        var isFirst = true;

        foreach (var embed in embeds)
        {
            var task = isFirst
                ? context.CreateResponseAsync(embed, ephemeral)
                : context.FollowUpAsync(embed, cancellationToken: cancellationToken);

            await task;

            isFirst = false;
        }
    }

    public static async Task FollowUpAsync(this InteractionContext context, DiscordEmbed embed, bool ephemeral = false, CancellationToken cancellationToken = default)
    {
        await context.FollowUpAsync(
            new DiscordFollowupMessageBuilder(
                new DiscordMessageBuilder().AddEmbed(embed))
            {
                IsEphemeral = ephemeral,
            });
    }

    public static async Task FollowUpAsync(this InteractionContext context, string content, bool ephemeral = false, CancellationToken cancellationToken = default)
    {
        await context.FollowUpAsync(
            new DiscordFollowupMessageBuilder(
                new DiscordMessageBuilder
                {
                    Content = content,
                })
            {
                IsEphemeral = ephemeral,
            });
    }
}