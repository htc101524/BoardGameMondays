using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Orchestrates sending review prompt emails to members for games they've played but not yet reviewed.
/// Prevents duplicate emails by tracking sent prompts in ReviewPromptSentEntity.
/// </summary>
public sealed class ReviewPromptService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IEmailSender _emailSender;

    public ReviewPromptService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IEmailSender emailSender)
    {
        _dbFactory = dbFactory;
        _emailSender = emailSender;
    }

    /// <summary>
    /// Gets games a member has played but not yet reviewed.
    /// </summary>
    /// <param name="memberId">The member to check</param>
    /// <param name="gameIds">Optional: only check these game IDs. If null, checks all games the member played.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of (GameId, Game) tuples for unreviewed games</returns>
    public async Task<List<(Guid GameId, BoardGameEntity Game)>> GetUnreviewedGamesAsync(
        Guid memberId,
        IEnumerable<Guid>? gameIds = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var gameIdList = gameIds?.ToList();

        // Get all games the member has played
        var playedGames = await db.GameNightGamePlayers
            .Where(p => p.MemberId == memberId)
            .Join(
                db.GameNightGames.Where(g => g.IsPlayed),
                p => p.GameNightGameId,
                g => g.Id,
                (p, g) => g.GameId)
            .Distinct()
            .ToListAsync(ct);

        if (!playedGames.Any())
            return [];

        // Filter to requested games if provided
        var gamesToCheck = gameIdList?.Any() == true
            ? playedGames.Where(g => gameIdList.Contains(g)).ToList()
            : playedGames;

        // Find reviews written by this member
        var reviewedGameIds = await db.Reviews
            .Where(r => r.ReviewerId == memberId)
            .Select(r => r.GameId)
            .ToListAsync(ct);

        // Get unreviewed games with details
        var unreviewedGameIds = gamesToCheck
            .Except(reviewedGameIds)
            .ToList();

        if (!unreviewedGameIds.Any())
            return [];

        var unreviewedGames = await db.Games
            .Where(g => unreviewedGameIds.Contains(g.Id))
            .ToListAsync(ct);

        return unreviewedGames
            .Select(g => (g.Id, g))
            .ToList();
    }

    /// <summary>
    /// Sends review prompt emails to all members who played games in a game night but haven't reviewed them.
    /// Sends one batch email per member listing all their unreviewed games from that night.
    /// </summary>
    /// <param name="gameNightId">The game night to process</param>
    /// <param name="delayHours">Hours to wait before sending (default: 24). 0 = send immediately.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of emails sent</returns>
    public async Task<int> SendReviewPromptsAsync(
        Guid gameNightId,
        int delayHours = 24,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Get the game night with all games and players
        var gameNight = await db.GameNights
            .Include(gn => gn.Games!)
            .ThenInclude(gng => gng.Game)
            .Include(gn => gn.Games!)
            .ThenInclude(gng => gng.Players!)
            .FirstOrDefaultAsync(gn => gn.Id == gameNightId, ct);

        if (gameNight is null)
            return 0;

        // Get only games that were actually played
        var playedGames = gameNight.Games?
            .Where(gng => gng.IsPlayed)
            .ToList() ?? [];

        if (!playedGames.Any())
            return 0;

        // Collect all players who participated
        var playerIds = playedGames
            .SelectMany(gng => gng.Players ?? [])
            .Select(p => p.MemberId)
            .Distinct()
            .ToList();

        if (!playerIds.Any())
            return 0;

        // Get member info for emails
        var members = await db.Members
            .Where(m => playerIds.Contains(m.Id))
            .ToListAsync(ct);

        var memberDict = members.ToDictionary(m => m.Id);
        var playedGameIds = playedGames.Select(g => g.GameId).ToList();

        int emailsSent = 0;

        // Send one batch email per member with their unreviewed games
        foreach (var memberId in playerIds)
        {
            try
            {
                if (!memberDict.TryGetValue(memberId, out var member))
                    continue;

                // Get unreviewed games for this member from THIS night
                var unreviewedGames = await GetUnreviewedGamesAsync(memberId, playedGameIds, ct);

                if (!unreviewedGames.Any())
                    continue;

                // Check if we've already sent a prompt for any of these games
                var alreadyPromptedGameIds = await db.ReviewPromptSents
                    .Where(rps => rps.MemberId == memberId && unreviewedGames.Select(ug => ug.GameId).Contains(rps.GameId))
                    .Select(rps => rps.GameId)
                    .ToListAsync(ct);

                var newUnreviewedGames = unreviewedGames
                    .Where(ug => !alreadyPromptedGameIds.Contains(ug.GameId))
                    .ToList();

                if (!newUnreviewedGames.Any())
                    continue;

                // Send email with all unreviewed games
                await SendReviewPromptEmailAsync(member, newUnreviewedGames, delayHours);

                // Record that we've sent prompts for these games
                foreach (var game in newUnreviewedGames)
                {
                    db.ReviewPromptSents.Add(new ReviewPromptSentEntity
                    {
                        Id = Guid.NewGuid(),
                        MemberId = memberId,
                        GameId = game.GameId,
                        SentOn = DateTimeOffset.UtcNow
                    });
                }

                await db.SaveChangesAsync(ct);
                emailsSent++;
            }
            catch (Exception ex)
            {
                // Log but don't crash - continue with next member
                System.Diagnostics.Debug.WriteLine($"Failed to send review prompt to member {memberId}: {ex.Message}");
            }
        }

        return emailsSent;
    }

    /// <summary>
    /// Sends the actual review prompt email to a member for their unreviewed games.
    /// Note: Email sending is fire-and-forget; failures are logged but not thrown.
    /// </summary>
    private async Task SendReviewPromptEmailAsync(
        MemberEntity member,
        List<(Guid GameId, BoardGameEntity Game)> unreviewedGames,
        int delayHours)
    {
        var delayMessage = delayHours > 0 ? $"(scheduled for {delayHours} hour(s) from now)" : "(sending now)";

        var htmlBody = BuildReviewPromptEmailHtml(member.Name, unreviewedGames);
        var subject = $"Share your thoughts - {unreviewedGames.Count} game{(unreviewedGames.Count == 1 ? "" : "s")} to review";

        try
        {
            // Fire and forget - EmailSender implementations handle retry/failure internally
            _ = Task.Run(async () =>
            {
                if (delayHours > 0)
                {
                    await Task.Delay(TimeSpan.FromHours(delayHours));
                }

                try
                {
                    await _emailSender.SendEmailAsync(member.Email, subject, htmlBody);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to send review prompt email to {member.Email}: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to schedule review prompt email: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the HTML email body for review prompts.
    /// </summary>
    private static string BuildReviewPromptEmailHtml(string memberName, List<(Guid GameId, BoardGameEntity Game)> games)
    {
        var gameListHtml = string.Join("\n", games.Select(g =>
            $@"<li>
                <strong>{HtmlEncode(g.Game.Name)}</strong>"
            + (string.IsNullOrWhiteSpace(g.Game.Tagline)
                ? ""
                : $@"<br/><small>{HtmlEncode(g.Game.Tagline)}</small>")
            + $@"
            </li>"));

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin-bottom: 20px; }}
        .content {{ margin: 20px 0; }}
        .game-list {{ margin: 15px 0; }}
        .game-list li {{ margin: 10px 0; }}
        .button {{ display: inline-block; background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; margin-top: 15px; }}
        .footer {{ font-size: 12px; color: #999; margin-top: 30px; border-top: 1px solid #eee; padding-top: 15px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h2>Share Your Feedback</h2>
            <p>Hi {HtmlEncode(memberName)},</p>
            <p>Thanks for playing with us! We'd love to hear what you thought about the following game(s):</p>
        </div>

        <div class=""content"">
            <ul class=""game-list"">
                {gameListHtml}
            </ul>
        </div>

        <p>Your reviews help our community discover great games. Every review counts!</p>

        <a href=""https://boardgamemondays.azurewebsites.net/games"" class=""button"">Write a Review</a>

        <div class=""footer"">
            <p>You received this email because you played a game that you haven't reviewed yet. Adjust your email preferences in your account settings if needed.</p>
        </div>
    </div>
</body>
</html>";
    }

    private static string HtmlEncode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
