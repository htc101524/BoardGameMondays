using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BoardGameMondays.Core;

/// <summary>
/// Service for GDPR data subject rights: export and deletion.
/// </summary>
public sealed class GdprService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ConsentService _consentService;

    public GdprService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ConsentService consentService)
    {
        _db = db;
        _userManager = userManager;
        _consentService = consentService;
    }

    /// <summary>
    /// Exports all personal data for a user in JSON format.
    /// </summary>
    public async Task<GdprDataExport> ExportUserDataAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            throw new InvalidOperationException("User not found.");
        }

        var claims = await _userManager.GetClaimsAsync(user);
        var displayName = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.DisplayName)?.Value;
        var memberIdStr = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.MemberId)?.Value;
        Guid? memberId = Guid.TryParse(memberIdStr, out var mid) ? mid : null;

        // Get member profile if linked
        MemberEntity? member = null;
        if (memberId.HasValue)
        {
            member = await _db.Members.FirstOrDefaultAsync(m => m.Id == memberId.Value, ct);
        }

        // Get all consent records
        var consents = await _consentService.GetAllConsentsForUserAsync(userId, ct);

        // Get betting history
        var bets = await _db.GameNightGameBets
            .Where(b => b.UserId == userId)
            .Select(b => new BetExport
            {
                GameNightGameId = b.GameNightGameId,
                PredictedWinnerId = b.PredictedWinnerMemberId,
                Amount = b.Amount,
                IsResolved = b.IsResolved,
                Payout = b.Payout,
                PlacedOn = b.CreatedOn
            })
            .ToListAsync(ct);

        // Get review agreements
        var agreements = await _db.ReviewAgreements
            .Where(a => a.UserId == userId)
            .Select(a => new ReviewAgreementExport
            {
                ReviewId = a.ReviewId,
                Score = a.Score,
                CreatedOn = a.CreatedOn
            })
            .ToListAsync(ct);

        // Get want-to-play votes
        var votes = await _db.WantToPlayVotes
            .Where(v => v.UserId == userId)
            .Select(v => new VoteExport
            {
                GameId = v.GameId,
                WeekKey = v.WeekKey,
                CreatedOn = v.CreatedOn
            })
            .ToListAsync(ct);

        // Get purchases
        var purchases = await _db.UserPurchases
            .Where(p => p.UserId == userId)
            .Select(p => new PurchaseExport
            {
                ShopItemId = p.ShopItemId,
                PurchasedOn = p.PurchasedOn
            })
            .ToListAsync(ct);

        return new GdprDataExport
        {
            ExportedOn = DateTimeOffset.UtcNow,
            Account = new AccountExport
            {
                UserId = userId,
                UserName = user.UserName,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                DisplayName = displayName,
                BgmCoins = user.BgmCoins,
                OddsDisplayFormat = user.OddsDisplayFormat.ToString()
            },
            Profile = member is null ? null : new ProfileExport
            {
                Name = member.Name,
                Email = member.Email,
                Summary = member.Summary,
                ProfileTagline = member.ProfileTagline,
                FavoriteGame = member.FavoriteGame,
                PlayStyle = member.PlayStyle,
                FunFact = member.FunFact,
                AvatarUrl = member.AvatarUrl,
                EloRating = member.EloRating
            },
            Consents = consents.Select(c => new ConsentExport
            {
                ConsentType = c.ConsentType,
                PolicyVersion = c.PolicyVersion,
                IsGranted = c.IsGranted,
                ConsentedOn = c.ConsentedOn
            }).ToList(),
            Bets = bets,
            ReviewAgreements = agreements,
            WantToPlayVotes = votes,
            Purchases = purchases
        };
    }

    /// <summary>
    /// Requests account deletion. Returns the scheduled deletion date.
    /// </summary>
    public async Task<DataDeletionRequestEntity> RequestAccountDeletionAsync(
        string userId,
        string email,
        string? reason,
        CancellationToken ct = default)
    {
        // Check for existing pending request
        var existing = await _db.DataDeletionRequests
            .Where(r => r.UserId == userId && r.Status == DeletionStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing;
        }

        var request = new DataDeletionRequestEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email,
            RequestedOn = DateTimeOffset.UtcNow,
            ScheduledDeletionOn = DateTimeOffset.UtcNow.AddDays(30),
            Status = DeletionStatus.Pending,
            Reason = reason?.Length > 1024 ? reason[..1024] : reason
        };

        _db.DataDeletionRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        return request;
    }

    /// <summary>
    /// Cancels a pending deletion request.
    /// </summary>
    public async Task<bool> CancelDeletionRequestAsync(string userId, CancellationToken ct = default)
    {
        var request = await _db.DataDeletionRequests
            .Where(r => r.UserId == userId && r.Status == DeletionStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (request is null)
        {
            return false;
        }

        request.Status = DeletionStatus.Cancelled;
        request.CancelledOn = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Gets the pending deletion request for a user, if any.
    /// </summary>
    public async Task<DataDeletionRequestEntity?> GetPendingDeletionRequestAsync(
        string userId,
        CancellationToken ct = default)
    {
        return await _db.DataDeletionRequests
            .Where(r => r.UserId == userId && r.Status == DeletionStatus.Pending)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Executes account deletion by anonymizing data.
    /// This should be called by a background job when scheduled deletion date is reached.
    /// </summary>
    public async Task ExecuteAccountDeletionAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return;
        }

        var claims = await _userManager.GetClaimsAsync(user);
        var memberIdStr = claims.FirstOrDefault(c => c.Type == BgmClaimTypes.MemberId)?.Value;
        Guid? memberId = Guid.TryParse(memberIdStr, out var mid) ? mid : null;

        // Anonymize member profile if exists
        if (memberId.HasValue)
        {
            var member = await _db.Members.FirstOrDefaultAsync(m => m.Id == memberId.Value, ct);
            if (member is not null)
            {
                var anonymousName = $"Deleted User {member.Id.ToString()[..8]}";
                member.Name = anonymousName;
                member.Email = string.Empty;
                member.Summary = null;
                member.ProfileTagline = null;
                member.FavoriteGame = null;
                member.PlayStyle = null;
                member.FunFact = null;
                member.AvatarUrl = null;
                member.IsBgmMember = false;
            }
        }

        // Delete user account (this removes auth data)
        await _userManager.DeleteAsync(user);

        // Update deletion request status
        var request = await _db.DataDeletionRequests
            .Where(r => r.UserId == userId && r.Status == DeletionStatus.Processing)
            .FirstOrDefaultAsync(ct);

        if (request is not null)
        {
            request.Status = DeletionStatus.Completed;
            request.CompletedOn = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}

#region Export DTOs

public sealed class GdprDataExport
{
    public DateTimeOffset ExportedOn { get; set; }
    public required AccountExport Account { get; set; }
    public ProfileExport? Profile { get; set; }
    public required List<ConsentExport> Consents { get; set; }
    public required List<BetExport> Bets { get; set; }
    public required List<ReviewAgreementExport> ReviewAgreements { get; set; }
    public required List<VoteExport> WantToPlayVotes { get; set; }
    public required List<PurchaseExport> Purchases { get; set; }
}

public sealed class AccountExport
{
    public required string UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? DisplayName { get; set; }
    public int BgmCoins { get; set; }
    public string? OddsDisplayFormat { get; set; }
}

public sealed class ProfileExport
{
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Summary { get; set; }
    public string? ProfileTagline { get; set; }
    public string? FavoriteGame { get; set; }
    public string? PlayStyle { get; set; }
    public string? FunFact { get; set; }
    public string? AvatarUrl { get; set; }
    public int EloRating { get; set; }
}

public sealed class ConsentExport
{
    public required string ConsentType { get; set; }
    public required string PolicyVersion { get; set; }
    public bool IsGranted { get; set; }
    public DateTimeOffset ConsentedOn { get; set; }
}

public sealed class BetExport
{
    public int GameNightGameId { get; set; }
    public Guid PredictedWinnerId { get; set; }
    public int Amount { get; set; }
    public bool IsResolved { get; set; }
    public int Payout { get; set; }
    public DateTimeOffset PlacedOn { get; set; }
}

public sealed class ReviewAgreementExport
{
    public Guid ReviewId { get; set; }
    public int Score { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}

public sealed class VoteExport
{
    public Guid GameId { get; set; }
    public int WeekKey { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
}

public sealed class PurchaseExport
{
    public Guid ShopItemId { get; set; }
    public DateTimeOffset PurchasedOn { get; set; }
}

#endregion
