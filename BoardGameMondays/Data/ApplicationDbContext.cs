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
    public DbSet<ReviewAgreementEntity> ReviewAgreements => Set<ReviewAgreementEntity>();
    public DbSet<FeaturedStateEntity> FeaturedState => Set<FeaturedStateEntity>();
    public DbSet<TicketEntity> Tickets => Set<TicketEntity>();
    public DbSet<TicketPriorityEntity> TicketPriorities => Set<TicketPriorityEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ReviewEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<TicketEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<ReviewAgreementEntity>()
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

        builder.Entity<TicketPriorityEntity>()
            .HasOne(x => x.Ticket)
            .WithMany(x => x.Priorities)
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TicketPriorityEntity>()
            .HasIndex(x => new { x.AdminUserId, x.Type, x.Rank })
            .IsUnique();

        builder.Entity<TicketPriorityEntity>()
            .HasIndex(x => new { x.AdminUserId, x.TicketId })
            .IsUnique();

        builder.Entity<ReviewAgreementEntity>()
            .HasOne(x => x.Review)
            .WithMany()
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ReviewAgreementEntity>()
            .HasIndex(x => new { x.UserId, x.ReviewId })
            .IsUnique();
    }
}
