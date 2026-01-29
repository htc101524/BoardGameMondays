using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Generates interesting stats for the Last Monday recap card.
/// Stats are calculated via SQL queries and one is picked at random if multiple are found.
/// </summary>
public sealed class RecapStatsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public RecapStatsService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Gets an interesting stat for the given game night date.
    /// Returns null if no interesting stat could be generated.
    /// </summary>
    public async Task<InterestingStat?> GetInterestingStatAsync(DateOnly gameNightDate, CancellationToken ct = default)
    {
        var stats = new List<InterestingStat>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var dateKey = GameNightService.ToDateKey(gameNightDate);

        // Get all game night date keys for querying (ordered descending by date)
        var allDateKeys = await db.GameNights
            .AsNoTracking()
            .OrderByDescending(n => n.DateKey)
            .Select(n => n.DateKey)
            .ToListAsync(ct);

        var currentIndex = allDateKeys.IndexOf(dateKey);
        if (currentIndex < 0)
        {
            return null;
        }

        // Stat 1: Consecutive wins for any member
        var consecutiveWinStats = await GetConsecutiveWinStatsAsync(db, dateKey, allDateKeys, ct);
        stats.AddRange(consecutiveWinStats);

        // Stat 2: Consecutive losses (no wins) for any member
        var losingStreakStats = await GetLosingStreakStatsAsync(db, dateKey, allDateKeys, ct);
        stats.AddRange(losingStreakStats);

        // Stat 3: Consecutive attendance for any member
        var attendanceStreakStats = await GetAttendanceStreakStatsAsync(db, dateKey, allDateKeys, ct);
        stats.AddRange(attendanceStreakStats);

        // Stat 4: First time attendance
        var firstTimeStats = await GetFirstTimeAttendanceStatsAsync(db, dateKey, ct);
        stats.AddRange(firstTimeStats);

        // Stat 5: Win after long losing streak
        var comebackStats = await GetComebackStatsAsync(db, dateKey, allDateKeys, ct);
        stats.AddRange(comebackStats);

        if (stats.Count == 0)
        {
            return null;
        }

        // Pick one at random
        var random = new Random();
        return stats[random.Next(stats.Count)];
    }

    private async Task<List<InterestingStat>> GetConsecutiveWinStatsAsync(
        ApplicationDbContext db,
        int currentDateKey,
        List<int> allDateKeys,
        CancellationToken ct)
    {
        var stats = new List<InterestingStat>();

        // Get winners from the current game night
        var currentWinners = await db.GameNightGames
            .AsNoTracking()
            .Include(g => g.GameNight)
            .Include(g => g.WinnerMember)
            .Where(g => g.GameNight.DateKey == currentDateKey && g.WinnerMemberId != null)
            .Select(g => new { g.WinnerMemberId, g.WinnerMember!.Name })
            .Distinct()
            .ToListAsync(ct);

        if (currentWinners.Count == 0)
        {
            return stats;
        }

        // Preload ALL wins and attendances for relevant members to avoid N+1
        var winnerIds = currentWinners.Select(w => w.WinnerMemberId!.Value).ToHashSet();
        
        var allWins = await db.GameNightGames
            .AsNoTracking()
            .Where(g => g.WinnerMemberId != null && winnerIds.Contains(g.WinnerMemberId.Value))
            .Select(g => new { g.WinnerMemberId, g.GameNight.DateKey })
            .ToListAsync(ct);

        var winsByMemberAndDate = allWins
            .GroupBy(w => w.WinnerMemberId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        var allAttendances = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => winnerIds.Contains(a.MemberId))
            .Select(a => new { a.MemberId, a.GameNight.DateKey })
            .ToListAsync(ct);

        var attendancesByMemberAndDate = allAttendances
            .GroupBy(a => a.MemberId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        foreach (var winner in currentWinners)
        {
            if (winner.WinnerMemberId is null) continue;

            var memberId = winner.WinnerMemberId.Value;
            var memberName = winner.Name;

            var memberWins = winsByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());
            var memberAttendances = attendancesByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());

            // Count consecutive weeks with at least one win (including current)
            var consecutiveWins = 1;
            var currentIndex = allDateKeys.IndexOf(currentDateKey);

            for (var i = currentIndex + 1; i < allDateKeys.Count; i++)
            {
                var previousDateKey = allDateKeys[i];
                var hadWin = memberWins.Contains(previousDateKey);

                if (hadWin)
                {
                    consecutiveWins++;
                }
                else
                {
                    // Check if they attended but didn't win - that breaks the streak
                    var attended = memberAttendances.Contains(previousDateKey);

                    if (attended)
                    {
                        break;
                    }
                    // If they didn't attend, we don't count it against them - continue checking earlier dates
                }
            }

            if (consecutiveWins >= 2)
            {
                var emoji = consecutiveWins >= 5 ? "🔥" : consecutiveWins >= 3 ? "🏆" : "⭐";
                stats.Add(new InterestingStat(
                    StatType.ConsecutiveWins,
                    memberName,
                    $"{emoji} {memberName} won for the {ToOrdinal(consecutiveWins)} consecutive week!",
                    consecutiveWins
                ));
            }
        }

        return stats;
    }

    private async Task<List<InterestingStat>> GetLosingStreakStatsAsync(
        ApplicationDbContext db,
        int currentDateKey,
        List<int> allDateKeys,
        CancellationToken ct)
    {
        var stats = new List<InterestingStat>();

        // Get attendees from current night who didn't win any game
        var currentAttendees = await db.GameNightAttendees
            .AsNoTracking()
            .Include(a => a.GameNight)
            .Include(a => a.Member)
            .Where(a => a.GameNight.DateKey == currentDateKey)
            .Select(a => new { a.MemberId, a.Member.Name })
            .ToListAsync(ct);

        var currentWinnerIds = await db.GameNightGames
            .AsNoTracking()
            .Include(g => g.GameNight)
            .Where(g => g.GameNight.DateKey == currentDateKey && g.WinnerMemberId != null)
            .Select(g => g.WinnerMemberId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var losers = currentAttendees.Where(a => !currentWinnerIds.Contains(a.MemberId)).ToList();

        if (losers.Count == 0)
        {
            return stats;
        }

        // Preload all attendances and wins for losers to avoid N+1
        var loserIds = losers.Select(l => l.MemberId).ToHashSet();
        
        var allAttendances = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => loserIds.Contains(a.MemberId))
            .Select(a => new { a.MemberId, a.GameNight.DateKey })
            .ToListAsync(ct);

        var attendancesByMemberAndDate = allAttendances
            .GroupBy(a => a.MemberId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        var allWins = await db.GameNightGames
            .AsNoTracking()
            .Where(g => g.WinnerMemberId != null && loserIds.Contains(g.WinnerMemberId.Value))
            .Select(g => new { g.WinnerMemberId, g.GameNight.DateKey })
            .ToListAsync(ct);

        var winsByMemberAndDate = allWins
            .GroupBy(w => w.WinnerMemberId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        foreach (var loser in losers)
        {
            var memberId = loser.MemberId;
            var memberName = loser.Name;

            var memberAttendances = attendancesByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());
            var memberWins = winsByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());

            // Count consecutive weeks attending without a win
            var losingStreak = 1;
            var currentIndex = allDateKeys.IndexOf(currentDateKey);

            for (var i = currentIndex + 1; i < allDateKeys.Count; i++)
            {
                var previousDateKey = allDateKeys[i];

                var attended = memberAttendances.Contains(previousDateKey);

                if (!attended)
                {
                    continue; // Skip weeks they didn't attend
                }

                var hadWin = memberWins.Contains(previousDateKey);

                if (hadWin)
                {
                    break; // Found a win, streak is broken
                }

                losingStreak++;
            }

            if (losingStreak >= 3)
            {
                var emoji = losingStreak >= 10 ? "😭" : losingStreak >= 5 ? "😢" : "😔";
                stats.Add(new InterestingStat(
                    StatType.LosingStreak,
                    memberName,
                    $"{emoji} {memberName} couldn't break their losing streak of {losingStreak} weeks",
                    losingStreak
                ));
            }
        }

        return stats;
    }

    private async Task<List<InterestingStat>> GetAttendanceStreakStatsAsync(
        ApplicationDbContext db,
        int currentDateKey,
        List<int> allDateKeys,
        CancellationToken ct)
    {
        var stats = new List<InterestingStat>();

        var currentAttendees = await db.GameNightAttendees
            .AsNoTracking()
            .Include(a => a.GameNight)
            .Include(a => a.Member)
            .Where(a => a.GameNight.DateKey == currentDateKey)
            .Select(a => new { a.MemberId, a.Member.Name })
            .ToListAsync(ct);

        if (currentAttendees.Count == 0)
        {
            return stats;
        }

        // Preload all attendances for current attendees to avoid N+1
        var attendeeIds = currentAttendees.Select(a => a.MemberId).ToHashSet();
        
        var allAttendances = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => attendeeIds.Contains(a.MemberId))
            .Select(a => new { a.MemberId, a.GameNight.DateKey })
            .ToListAsync(ct);

        var attendancesByMemberAndDate = allAttendances
            .GroupBy(a => a.MemberId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        foreach (var attendee in currentAttendees)
        {
            var memberId = attendee.MemberId;
            var memberName = attendee.Name;

            var memberAttendances = attendancesByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());

            // Count consecutive weeks attending (including current)
            var consecutiveAttendance = 1;
            var currentIndex = allDateKeys.IndexOf(currentDateKey);

            for (var i = currentIndex + 1; i < allDateKeys.Count; i++)
            {
                var previousDateKey = allDateKeys[i];

                var attended = memberAttendances.Contains(previousDateKey);

                if (attended)
                {
                    consecutiveAttendance++;
                }
                else
                {
                    break;
                }
            }

            if (consecutiveAttendance >= 5)
            {
                var emoji = consecutiveAttendance >= 10 ? "🌟" : "📅";
                stats.Add(new InterestingStat(
                    StatType.AttendanceStreak,
                    memberName,
                    $"{emoji} This was {memberName}'s {ToOrdinal(consecutiveAttendance)} consecutive appearance!",
                    consecutiveAttendance
                ));
            }
        }

        return stats;
    }

    private async Task<List<InterestingStat>> GetFirstTimeAttendanceStatsAsync(
        ApplicationDbContext db,
        int currentDateKey,
        CancellationToken ct)
    {
        var stats = new List<InterestingStat>();

        // Get current attendees
        var currentAttendees = await db.GameNightAttendees
            .AsNoTracking()
            .Include(a => a.GameNight)
            .Include(a => a.Member)
            .Where(a => a.GameNight.DateKey == currentDateKey)
            .Select(a => new { a.MemberId, a.Member.Name })
            .ToListAsync(ct);

        if (currentAttendees.Count == 0)
        {
            return stats;
        }

        // Preload all previous attendances for these members
        var attendeeIds = currentAttendees.Select(a => a.MemberId).ToHashSet();
        
        var previousAttendances = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => attendeeIds.Contains(a.MemberId) && a.GameNight.DateKey < currentDateKey)
            .Select(a => a.MemberId)
            .Distinct()
            .ToListAsync(ct);

        var membersWithPreviousAttendance = previousAttendances.ToHashSet();

        foreach (var attendee in currentAttendees)
        {
            var memberId = attendee.MemberId;
            var memberName = attendee.Name;

            // Check if they have any previous attendance
            if (!membersWithPreviousAttendance.Contains(memberId))
            {
                stats.Add(new InterestingStat(
                    StatType.FirstTimeAttendance,
                    memberName,
                    $"🎉 This was {memberName}'s first time at a Monday!",
                    1
                ));
            }
        }

        return stats;
    }

    private async Task<List<InterestingStat>> GetComebackStatsAsync(
        ApplicationDbContext db,
        int currentDateKey,
        List<int> allDateKeys,
        CancellationToken ct)
    {
        var stats = new List<InterestingStat>();

        // Get winners from current night
        var currentWinners = await db.GameNightGames
            .AsNoTracking()
            .Include(g => g.GameNight)
            .Include(g => g.WinnerMember)
            .Where(g => g.GameNight.DateKey == currentDateKey && g.WinnerMemberId != null)
            .Select(g => new { g.WinnerMemberId, g.WinnerMember!.Name })
            .Distinct()
            .ToListAsync(ct);

        if (currentWinners.Count == 0)
        {
            return stats;
        }

        var currentIndex = allDateKeys.IndexOf(currentDateKey);

        // Preload all attendances and wins for winners to avoid N+1
        var winnerIds = currentWinners.Select(w => w.WinnerMemberId!.Value).ToHashSet();
        
        var allAttendances = await db.GameNightAttendees
            .AsNoTracking()
            .Where(a => winnerIds.Contains(a.MemberId))
            .Select(a => new { a.MemberId, a.GameNight.DateKey })
            .ToListAsync(ct);

        var attendancesByMemberAndDate = allAttendances
            .GroupBy(a => a.MemberId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        var allWins = await db.GameNightGames
            .AsNoTracking()
            .Where(g => g.WinnerMemberId != null && winnerIds.Contains(g.WinnerMemberId.Value))
            .Select(g => new { g.WinnerMemberId, g.GameNight.DateKey })
            .ToListAsync(ct);

        var winsByMemberAndDate = allWins
            .GroupBy(w => w.WinnerMemberId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.DateKey).ToHashSet());

        foreach (var winner in currentWinners)
        {
            if (winner.WinnerMemberId is null) continue;

            var memberId = winner.WinnerMemberId.Value;
            var memberName = winner.Name;

            var memberAttendances = attendancesByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());
            var memberWins = winsByMemberAndDate.GetValueOrDefault(memberId, new HashSet<int>());

            // Count how many consecutive weeks (before this one) they attended without winning
            var droughtLength = 0;

            for (var i = currentIndex + 1; i < allDateKeys.Count; i++)
            {
                var previousDateKey = allDateKeys[i];

                var attended = memberAttendances.Contains(previousDateKey);

                if (!attended)
                {
                    continue; // Skip weeks they didn't attend
                }

                var hadWin = memberWins.Contains(previousDateKey);

                if (hadWin)
                {
                    break; // Found their last win
                }

                droughtLength++;
            }

            if (droughtLength >= 5)
            {
                stats.Add(new InterestingStat(
                    StatType.Comeback,
                    memberName,
                    $"🎊 {memberName} finally won after a {droughtLength}-week drought!",
                    droughtLength
                ));
            }
        }

        return stats;
    }

    private static string ToOrdinal(int value)
    {
        if (value % 100 is 11 or 12 or 13)
        {
            return value + "th";
        }

        return (value % 10) switch
        {
            1 => value + "st",
            2 => value + "nd",
            3 => value + "rd",
            _ => value + "th"
        };
    }

    public sealed record InterestingStat(StatType Type, string MemberName, string Message, int Value);

    public enum StatType
    {
        ConsecutiveWins,
        LosingStreak,
        AttendanceStreak,
        FirstTimeAttendance,
        Comeback
    }
}
