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
    public DbSet<GameNightEntity> GameNights => Set<GameNightEntity>();
    public DbSet<GameNightAttendeeEntity> GameNightAttendees => Set<GameNightAttendeeEntity>();
    public DbSet<GameNightRsvpEntity> GameNightRsvps => Set<GameNightRsvpEntity>();
    public DbSet<GameNightGameEntity> GameNightGames => Set<GameNightGameEntity>();
    public DbSet<GameNightGamePlayerEntity> GameNightGamePlayers => Set<GameNightGamePlayerEntity>();
    public DbSet<GameNightGameTeamEntity> GameNightGameTeams => Set<GameNightGameTeamEntity>();
    public DbSet<GameNightGameOddsEntity> GameNightGameOdds => Set<GameNightGameOddsEntity>();
    public DbSet<GameNightGameBetEntity> GameNightGameBets => Set<GameNightGameBetEntity>();
    public DbSet<VictoryRouteEntity> VictoryRoutes => Set<VictoryRouteEntity>();
    public DbSet<VictoryRouteOptionEntity> VictoryRouteOptions => Set<VictoryRouteOptionEntity>();
    public DbSet<GameNightGameVictoryRouteValueEntity> GameNightGameVictoryRouteValues => Set<GameNightGameVictoryRouteValueEntity>();
    public DbSet<BlogPostEntity> BlogPosts => Set<BlogPostEntity>();
    public DbSet<WantToPlayVoteEntity> WantToPlayVotes => Set<WantToPlayVoteEntity>();

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

        builder.Entity<TicketEntity>()
            .Property(x => x.DoneOn)
            .HasConversion(
                toDb => toDb.HasValue ? toDb.Value.UtcDateTime.Ticks : (long?)null,
                fromDb => fromDb.HasValue ? new DateTimeOffset(new DateTime(fromDb.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null);

        builder.Entity<ReviewAgreementEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightAttendeeEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightRsvpEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightGameEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightGameEntity>()
            .Property(x => x.WinnerTeamName)
            .HasMaxLength(64);

        builder.Entity<GameNightGameTeamEntity>()
            .Property(x => x.TeamName)
            .HasMaxLength(64);

        builder.Entity<GameNightGameTeamEntity>()
            .Property(x => x.ColorHex)
            .HasMaxLength(16);

        builder.Entity<GameNightGamePlayerEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightGamePlayerEntity>()
            .Property(x => x.TeamName)
            .HasMaxLength(64);

        builder.Entity<GameNightGameOddsEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightGameBetEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<GameNightGameBetEntity>()
            .Property(x => x.ResolvedOn)
            .HasConversion(
                toDb => toDb.HasValue ? toDb.Value.UtcDateTime.Ticks : (long?)null,
                fromDb => fromDb.HasValue ? new DateTimeOffset(new DateTime(fromDb.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null);

        builder.Entity<BlogPostEntity>()
            .Property(x => x.CreatedOn)
            .HasConversion(
                toDb => toDb.UtcDateTime.Ticks,
                fromDb => new DateTimeOffset(new DateTime(fromDb, DateTimeKind.Utc)));

        builder.Entity<WantToPlayVoteEntity>()
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

        builder.Entity<WantToPlayVoteEntity>()
            .HasIndex(x => new { x.UserId, x.GameId, x.WeekKey })
            .IsUnique();

        builder.Entity<WantToPlayVoteEntity>()
            .HasIndex(x => new { x.UserId, x.WeekKey });

        builder.Entity<WantToPlayVoteEntity>()
            .HasIndex(x => x.GameId);

        builder.Entity<GameNightEntity>()
            .HasIndex(x => x.DateKey)
            .IsUnique();

        builder.Entity<GameNightAttendeeEntity>()
            .HasOne(x => x.GameNight)
            .WithMany(x => x.Attendees)
            .HasForeignKey(x => x.GameNightId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightAttendeeEntity>()
            .HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightAttendeeEntity>()
            .HasIndex(x => new { x.GameNightId, x.MemberId })
            .IsUnique();

        builder.Entity<GameNightRsvpEntity>()
            .HasOne(x => x.GameNight)
            .WithMany(x => x.Rsvps)
            .HasForeignKey(x => x.GameNightId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightRsvpEntity>()
            .HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightRsvpEntity>()
            .HasIndex(x => new { x.GameNightId, x.MemberId })
            .IsUnique();

        builder.Entity<GameNightGameEntity>()
            .HasOne(x => x.GameNight)
            .WithMany(x => x.Games)
            .HasForeignKey(x => x.GameNightId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameEntity>()
            .HasOne(x => x.Game)
            .WithMany()
            .HasForeignKey(x => x.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameEntity>()
            .HasIndex(x => new { x.GameNightId, x.GameId })
            .IsUnique();

        builder.Entity<GameNightGameEntity>()
            .HasOne(x => x.WinnerMember)
            .WithMany()
            .HasForeignKey(x => x.WinnerMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<GameNightGamePlayerEntity>()
            .HasOne(x => x.GameNightGame)
            .WithMany(x => x.Players)
            .HasForeignKey(x => x.GameNightGameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGamePlayerEntity>()
            .HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGamePlayerEntity>()
            .HasIndex(x => new { x.GameNightGameId, x.MemberId })
            .IsUnique();

        builder.Entity<GameNightGameTeamEntity>()
            .HasOne(x => x.GameNightGame)
            .WithMany(x => x.Teams)
            .HasForeignKey(x => x.GameNightGameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameTeamEntity>()
            .HasIndex(x => new { x.GameNightGameId, x.TeamName })
            .IsUnique();

        builder.Entity<GameNightGameOddsEntity>()
            .HasOne(x => x.GameNightGame)
            .WithMany(x => x.Odds)
            .HasForeignKey(x => x.GameNightGameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameOddsEntity>()
            .HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameOddsEntity>()
            .HasIndex(x => new { x.GameNightGameId, x.MemberId })
            .IsUnique();

        builder.Entity<GameNightGameBetEntity>()
            .HasOne(x => x.GameNightGame)
            .WithMany(x => x.Bets)
            .HasForeignKey(x => x.GameNightGameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameBetEntity>()
            .HasOne(x => x.PredictedWinnerMember)
            .WithMany()
            .HasForeignKey(x => x.PredictedWinnerMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GameNightGameBetEntity>()
            .HasIndex(x => new { x.GameNightGameId, x.UserId })
            .IsUnique();

        builder.Entity<VictoryRouteEntity>()
            .HasOne(x => x.Game)
            .WithMany(x => x.VictoryRoutes)
            .HasForeignKey(x => x.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<VictoryRouteEntity>()
            .HasIndex(x => new { x.GameId, x.SortOrder })
            .IsUnique();

        builder.Entity<VictoryRouteOptionEntity>()
            .HasOne(x => x.VictoryRoute)
            .WithMany(x => x.Options)
            .HasForeignKey(x => x.VictoryRouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<VictoryRouteOptionEntity>()
            .HasIndex(x => new { x.VictoryRouteId, x.SortOrder })
            .IsUnique();

        builder.Entity<GameNightGameVictoryRouteValueEntity>()
            .HasOne(x => x.GameNightGame)
            .WithMany(x => x.VictoryRouteValues)
            .HasForeignKey(x => x.GameNightGameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GameNightGameVictoryRouteValueEntity>()
            .HasOne(x => x.VictoryRoute)
            .WithMany()
            .HasForeignKey(x => x.VictoryRouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GameNightGameVictoryRouteValueEntity>()
            .HasIndex(x => new { x.GameNightGameId, x.VictoryRouteId })
            .IsUnique();

        builder.Entity<BlogPostEntity>()
            .HasIndex(x => x.Slug)
            .IsUnique();
    }
}
