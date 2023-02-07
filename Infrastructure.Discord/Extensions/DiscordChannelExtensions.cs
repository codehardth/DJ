using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace Infrastructure.Discord.Extensions;

public static class DiscordChannelExtensions
{
    /// <summary>
    /// Send message that will automatically delete after timeout period.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="message"></param>
    /// <param name="deleteInMs"></param>
    /// <returns></returns>
    public static async Task SendDisposableMessageAsync(this CommandContext commandContext, string message, int deleteInMs = 10000, CancellationToken cancellationToken = default)
    {
        var sentMessage = await commandContext.RespondAsync(message);

        _ = Task.Delay(deleteInMs)
            .ContinueWith(async (arg) => { await sentMessage.DeleteAsync(); });
    }

    /// <summary>
    /// Send message that will automatically delete after timeout period.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="message"></param>
    /// <param name="deleteInMs"></param>
    /// <returns></returns>
    public static async Task SendDisposableMessageAsync(this DiscordChannel channel, string message, int deleteInMs = 10000, CancellationToken cancellationToken = default)
    {
        var sentMessage = await channel.SendMessageAsync(message);
        _ = Task.Delay(deleteInMs)
            .ContinueWith(async (arg) => { await sentMessage.DeleteAsync(); });
    }
}