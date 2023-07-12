namespace DJ.Domain.Entities;

public partial class Member
{
    public Member()
    {
        // Entity Framework Constructor
    }

    private Member(
        ulong id,
        ulong guildId,
        DateTimeOffset createdAt)
    {
        this.Id = id;
        this.GuildId = guildId;
        this.CreatedAt = createdAt;
    }

    public ulong Id { get; }

    public ulong GuildId { get; }

    public DateTimeOffset CreatedAt { get; }

    public static Member Create(
        ulong id,
        ulong guildId)
        => new(id, guildId, DateTimeOffset.UtcNow);
}