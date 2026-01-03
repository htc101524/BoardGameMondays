using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BoardGameMondays.Data.Entities;

namespace BoardGameMondays.Data;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<MemberEntity> Members => Set<MemberEntity>();
    public DbSet<BoardGameEntity> Games => Set<BoardGameEntity>();
    public DbSet<ReviewEntity> Reviews => Set<ReviewEntity>();
    public DbSet<FeaturedStateEntity> FeaturedState => Set<FeaturedStateEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ReviewEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<MemberEntity>()
            .HasIndex(x => x.Name)
            .IsUnique();

        builder.Entity<ReviewEntity>()
            .HasOne(x => x.Game)
            .WithMany(x => x.Reviews)
            .HasForeignKey(x => x.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ReviewEntity>()
            .HasOne(x => x.Reviewer)
            .WithMany()
            .HasForeignKey(x => x.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
