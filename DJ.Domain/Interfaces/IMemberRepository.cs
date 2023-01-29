using DJ.Domain.Entities;

namespace DJ.Domain.Interfaces;

public interface IMemberRepository : IRepository<Member>
{
    Task AddNewMembersAsync(IEnumerable<Member> members, CancellationToken cancellationToken = default);
}