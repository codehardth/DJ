using DJ.Domain.Interfaces;
using DJ.Infrastructure;
using DJ.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static void AddDjDbContext(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DjDbContext>(options =>
            options.UseSqlite(connectionString)
                .UseLazyLoadingProxies());
    }

    public static void AddRepositories(this IServiceCollection services)
    {
        services.TryAddScoped<IMemberRepository, MemberRepository>();
    }
}