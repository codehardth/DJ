using DJ.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DJ.Infrastructure;

public class DjDbContext : DbContext
{
    public DjDbContext(DbContextOptions<DjDbContext> options)
        : base(options)
    {
    }

    public DbSet<Member> Members { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureMember(modelBuilder);
    }

    private static void ConfigureMember(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>(builder =>
        {
            builder.HasKey(m => m.Id);
            builder.Property(m => m.GuildId)
                .IsRequired();
            builder.Property(m => m.CreatedAt)
                .IsRequired();
        });
    }
}