using System.Runtime.Caching;
using DJ.Domain.Entities;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Infrastructure.Discord;

namespace Codehard.DJ;

public partial class DjCommandModule
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

        _cache.Add(key, expireTime, new CacheItemPolicy
        {
            AbsoluteExpiration = expireTime,
        });
    }

    private async Task PerformWithThrottlePolicy(
        InteractionContext context,
        Func<Member, string> keyFunc,
        Func<InteractionContext, Member, Task> func)
    {
        var member = await GetMemberAsync(context);

        if (member == null)
        {
            await ReactAsync(context, Emojis.ThumbsDown);

            return;
        }

        var banKey = GetBanKey(member.Id);

        if (this.TryGetCache(banKey, out DateTimeOffset banLiftDateTime))
        {
            await ReactAsync(context, Emojis.NoEntry);

            await context.CreateResponseAsync(
                $"You're banned from using the bot, " +
                $"please try again in {(int)banLiftDateTime.Subtract(DateTimeOffset.UtcNow).TotalSeconds} second(s).");

            return;
        }

        var key = keyFunc(member);

        if (this.TryGetCache(key, out DateTimeOffset expirationDateTimeOffset))
        {
            await ReactAsync(context, Emojis.NoEntry);

            await context.CreateResponseAsync(
                $"You're being throttled, " +
                $"please try again in {(int)expirationDateTimeOffset.Subtract(DateTimeOffset.UtcNow).TotalSeconds} second(s).");

            return;
        }

        var expireTime = DateTimeOffset.UtcNow.AddSeconds(this._cooldown);

        await func(context, member);

        _cache.Add(key, expireTime, new CacheItemPolicy
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
        if (!_cache.Contains(key))
        {
            value = default!;

            return false;
        }

        value = (T)_cache[key];

        return true;
    }

    private static Task ReactAsync(InteractionContext ctx, string emojiName)
    {
        // return ctx.Message.CreateReactionAsync(DiscordEmoji.FromName(ctx.Client, emojiName));

        return ctx.CreateResponseAsync($"{ctx.User.Mention} {emojiName}");
    }

    private static Task ReactAsync(DiscordClient client, DiscordChannel channel, DiscordUser user, string emojiName)
    {
        return client.SendMessageAsync(channel, $"{user.Mention} {emojiName}");
    }

    private Task<Member?> GetMemberAsync(InteractionContext context)
    {
        var discordMember = context.Member ?? context.User;

        return GetMemberAsync(discordMember);
    }

    private async Task<Member?> GetMemberAsync(DiscordUser discordUser)
    {
        var member = await this._memberRepository.GetByIdAsync(discordUser.Id);

        return member;
    }

    private static bool TryGetUserId(string mentionText, out ulong id)
    {
        var formattedText = mentionText.Replace("<@", "").Replace(">", "");

        return ulong.TryParse(formattedText, out id);
    }

    private static string GetBanKey(ulong id)
        => $"ban-{id}";
}