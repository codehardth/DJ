using DJ.Domain.Entities;
using DJ.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DJ.Infrastructure.Repositories;

internal sealed class MemberRepository : GenericRepository<Member>, IMemberRepository
{
    private readonly DjDbContext _dbContext;

    public MemberRepository(DjDbContext dbContext)
        : base(dbContext)
    {
        this._dbContext = dbContext;
    }

    protected override DbSet<Member> Set => this._dbContext.Members;

    public Task AddNewMembersAsync(IEnumerable<Member> members, CancellationToken cancellationToken = default)
    {
        var existingMembers = this.Set.Select(m => m.Id).AsEnumerable();

        var newMembers = members.ExceptBy(existingMembers, m => m.Id).ToArray();

        return
            !newMembers.Any()
                ? Task.CompletedTask
                : this.AddRangeAsync(newMembers, cancellationToken);
    }
}