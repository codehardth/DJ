using System.Runtime.Caching;
using DJ.Domain.Entities;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using Infrastructure.Discord;

namespace Codehard.DJ;

public partial class DjCommandHandler
{
    private async Task PerformWithThrottlePolicy(
        DiscordClient client,
        DiscordChannel channel,
        DiscordUser discordUser,
        Func<Member, string> keyFunc,
        Func<DiscordClient, DiscordChannel, DiscordUser, Member, Task> func)
    {
        var member = await GetMemberAsync(discordUser);

        if (member == null)
        {
            await client.SendMessageAsync(channel, $"{discordUser.Mention} {Emojis.ThumbsDown}");

            return;
        }

        var key = keyFunc(member);

        if (this.TryGetCache(key, out DateTimeOffset expirationDateTimeOffset))
        {
            await client.SendMessageAsync(
                channel,
                $"{Emojis.NoEntry} {discordUser.Mention} You're being throttled, " +
                $"please try again in {(int)expirationDateTimeOffset.Subtract(DateTimeOffset.UtcNow).TotalSeconds} second(s).");

            return;
        }

        var expireTime = DateTimeOffset.UtcNow.AddSeconds(this._cooldown);

        await func(client, channel, discordUser, member);

        this._cache.Add(key, expireTime, new CacheItemPolicy
        {
            AbsoluteExpiration = expireTime,
        });
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

        var expireTime = DateTimeOffset.UtcNow.AddSeconds(this._cooldown);

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

    private static Task ReactAsync(DiscordClient client, DiscordChannel channel, DiscordUser user, string emojiName)
    {
        return client.SendMessageAsync(channel, $"{user.Mention} {emojiName}");
    }

    private Task<Member?> GetMemberAsync(CommandContext context)
    {
        var discordMember = context.Member ?? context.User;

        return GetMemberAsync(discordMember);
    }

    private async Task<Member?> GetMemberAsync(DiscordUser discordUser)
    {
        var member = await this._memberRepository.GetByIdAsync(discordUser.Id);

        return member;
    }
}