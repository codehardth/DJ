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

            builder.OwnsMany(m => m.PlayedTracks, ConfigurePlayedTrack);
        });
    }

    private static void ConfigurePlayedTrack(OwnedNavigationBuilder<Member, PlayedTrack> builder)
    {
        builder.ToTable("PlayedTracks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TrackId)
            .IsRequired();
        builder.Property(t => t.Title)
            .IsRequired();
        builder.Property(t => t.Artists)
            .HasConversion(
                mem => string.Join(',', mem),
                db => db.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .IsRequired();
        builder.Property(t => t.Album)
            .IsRequired();
        builder.Property(t => t.Genres)
            .HasConversion(
                mem => string.Join(',', mem),
                db => db.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .IsRequired();
        builder.Property(t => t.Score)
            .IsRequired();
        builder.Property(t => t.Uri);
        builder.Property(t => t.ConsiderInappropriate)
            .IsRequired()
            .HasDefaultValue(false);
        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Metadata.PrincipalToDependent!.SetField(Member.PlayedTracksProperty);
    }
}