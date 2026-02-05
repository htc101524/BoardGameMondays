using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Service for managing GDPR consent records.
/// </summary>
public sealed class ConsentService
{
    private readonly ApplicationDbContext _db;

    /// <summary>
    /// Current version of the privacy policy. Increment when policy changes.
    /// </summary>
    public const string PrivacyPolicyVersion = "1.0";

    /// <summary>
    /// Current version of the terms of service. Increment when terms change.
    /// </summary>
    public const string TermsOfServiceVersion = "1.0";

    /// <summary>
    /// Current version of the cookie policy. Increment when policy changes.
    /// </summary>
    public const string CookiePolicyVersion = "1.0";

    public ConsentService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Records a consent decision.
    /// </summary>
    public async Task RecordConsentAsync(
        string? userId,
        string? anonymousId,
        string consentType,
        string policyVersion,
        bool isGranted,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var consent = new UserConsentEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AnonymousId = anonymousId,
            ConsentType = consentType,
            PolicyVersion = policyVersion,
            IsGranted = isGranted,
            ConsentedOn = DateTimeOffset.UtcNow,
            IpAddress = ipAddress?.Length > 45 ? ipAddress[..45] : ipAddress,
            UserAgent = userAgent?.Length > 512 ? userAgent[..512] : userAgent
        };

        _db.UserConsents.Add(consent);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records cookie consent from the cookie banner.
    /// </summary>
    public async Task RecordCookieConsentAsync(
        string? userId,
        string? anonymousId,
        bool essentialCookies,
        bool analyticsCookies,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Essential cookies are always required
        await RecordConsentAsync(userId, anonymousId, ConsentTypes.EssentialCookies, CookiePolicyVersion, true, ipAddress, userAgent, ct);
        
        // Analytics cookies are optional
        await RecordConsentAsync(userId, anonymousId, ConsentTypes.AnalyticsCookies, CookiePolicyVersion, analyticsCookies, ipAddress, userAgent, ct);
    }

    /// <summary>
    /// Records registration consent (privacy policy + terms).
    /// </summary>
    public async Task RecordRegistrationConsentAsync(
        string userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        await RecordConsentAsync(userId, null, ConsentTypes.PrivacyPolicy, PrivacyPolicyVersion, true, ipAddress, userAgent, ct);
        await RecordConsentAsync(userId, null, ConsentTypes.TermsOfService, TermsOfServiceVersion, true, ipAddress, userAgent, ct);
    }

    /// <summary>
    /// Gets the latest consent record for a user and consent type.
    /// </summary>
    public async Task<UserConsentEntity?> GetLatestConsentAsync(
        string userId,
        string consentType,
        CancellationToken ct = default)
    {
        return await _db.UserConsents
            .Where(c => c.UserId == userId && c.ConsentType == consentType)
            .OrderByDescending(c => c.ConsentedOn)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Checks if user has consented to the current version of a policy.
    /// </summary>
    public async Task<bool> HasCurrentConsentAsync(
        string userId,
        string consentType,
        string currentVersion,
        CancellationToken ct = default)
    {
        var latest = await GetLatestConsentAsync(userId, consentType, ct);
        return latest is not null && latest.IsGranted && latest.PolicyVersion == currentVersion;
    }

    /// <summary>
    /// Gets all consent records for a user (for data export).
    /// </summary>
    public async Task<List<UserConsentEntity>> GetAllConsentsForUserAsync(
        string userId,
        CancellationToken ct = default)
    {
        return await _db.UserConsents
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.ConsentedOn)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Links anonymous consent records to a user after registration/login.
    /// </summary>
    public async Task LinkAnonymousConsentsToUserAsync(
        string anonymousId,
        string userId,
        CancellationToken ct = default)
    {
        var anonymousConsents = await _db.UserConsents
            .Where(c => c.AnonymousId == anonymousId && c.UserId == null)
            .ToListAsync(ct);

        foreach (var consent in anonymousConsents)
        {
            consent.UserId = userId;
        }

        await _db.SaveChangesAsync(ct);
    }
}
