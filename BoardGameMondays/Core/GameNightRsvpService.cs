using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Manages RSVP and attendance for game nights.
/// Separate service for clarity: RSVPs represent intent, Attendees represent actual attendance.
/// </summary>
public sealed class GameNightRsvpService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly GameNightService _gameNightService;

    public GameNightRsvpService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        GameNightService gameNightService)
    {
        _dbFactory = dbFactory;
        _gameNightService = gameNightService;
    }

    /// <summary>
    /// Records RSVP (intent to attend or not).
    /// - Keeps the Attendees list in sync (IsAttending=true => attendee exists; false => attendee removed).
    /// </summary>
    public async Task<GameNightService.GameNight?> SetRsvpAsync(Guid gameNightId, Guid memberId, bool attending, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Verify the game night exists
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        // Verify the member actually exists to prevent invalid RSVPs
        var member = await db.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null)
        {
            return null;
        }

        if (!member.IsBgmMember)
        {
            throw new InvalidOperationException("You can't RSVP without member status. Contact one of the other members to get involved.");
        }

        var now = DateTimeOffset.UtcNow;

        var rsvp = await db.GameNightRsvps
            .FirstOrDefaultAsync(r => r.GameNightId == gameNightId && r.MemberId == memberId, ct);

        if (rsvp is null)
        {
            rsvp = new GameNightRsvpEntity
            {
                GameNightId = gameNightId,
                MemberId = memberId,
                IsAttending = attending,
                CreatedOn = now
            };
            db.GameNightRsvps.Add(rsvp);
        }
        else
        {
            rsvp.IsAttending = attending;
            rsvp.CreatedOn = now;
        }

        // Keep attendee table in sync.
        var existing = await db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attending)
        {
            if (existing is null)
            {
                db.GameNightAttendees.Add(new GameNightAttendeeEntity
                {
                    GameNightId = gameNightId,
                    MemberId = memberId,
                    CreatedOn = now
                });
            }
        }
        else
        {
            if (existing is not null)
            {
                db.GameNightAttendees.Remove(existing);
            }
        }

        await db.SaveChangesAsync(ct);
        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<bool> IsBgmMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Members
            .AsNoTracking()
            .AnyAsync(m => m.Id == memberId && m.IsBgmMember, ct);
    }

    /// <summary>
    /// Updates attendance without changing RSVP intent.
    /// Intended for admin after-the-fact corrections on past nights.
    /// </summary>
    public async Task<GameNightService.GameNight?> SetAttendanceAsync(Guid gameNightId, Guid memberId, bool attending, bool respectExplicitDeclines = false, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        if (attending && respectExplicitDeclines)
        {
            var declined = await db.GameNightRsvps
                .AsNoTracking()
                .AnyAsync(r => r.GameNightId == gameNightId && r.MemberId == memberId && !r.IsAttending, ct);

            if (declined)
            {
                return await _gameNightService.GetByIdAsync(gameNightId, ct);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attending)
        {
            if (existing is null)
            {
                db.GameNightAttendees.Add(new GameNightAttendeeEntity
                {
                    GameNightId = gameNightId,
                    MemberId = memberId,
                    CreatedOn = now
                });
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            if (existing is not null)
            {
                db.GameNightAttendees.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
        }

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    public async Task<GameNightService.GameNight?> SetSnackBroughtAsync(Guid gameNightId, Guid memberId, string? snackBrought, CancellationToken ct = default)
    {
        snackBrought = InputGuards.OptionalTrimToNull(snackBrought, maxLength: 128, nameof(snackBrought));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.GameNights.AnyAsync(n => n.Id == gameNightId, ct);
        if (!exists)
        {
            return null;
        }

        var attendee = await db.GameNightAttendees
            .FirstOrDefaultAsync(a => a.GameNightId == gameNightId && a.MemberId == memberId, ct);

        if (attendee is null)
        {
            return await _gameNightService.GetByIdAsync(gameNightId, ct);
        }

        attendee.SnackBrought = snackBrought;
        await db.SaveChangesAsync(ct);

        return await _gameNightService.GetByIdAsync(gameNightId, ct);
    }

    /// <summary>
    /// Backward-compatibility wrapper around SetAttendanceAsync.
    /// </summary>
    public Task<GameNightService.GameNight?> SetAttendingAsync(Guid gameNightId, Guid memberId, bool attending, CancellationToken ct = default)
        => SetAttendanceAsync(gameNightId, memberId, attending, ct: ct);
}
