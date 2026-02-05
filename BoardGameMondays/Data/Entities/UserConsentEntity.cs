using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

/// <summary>
/// Tracks user consent for GDPR compliance (privacy policy, terms, cookies, marketing).
/// </summary>
public sealed class UserConsentEntity
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// The user who gave consent. Null for anonymous cookie consent.
    /// </summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>
    /// Browser fingerprint or session ID for anonymous consent tracking.
    /// </summary>
    [MaxLength(128)]
    public string? AnonymousId { get; set; }

    /// <summary>
    /// Type of consent given (e.g., "PrivacyPolicy", "TermsOfService", "EssentialCookies", "AnalyticsCookies", "Marketing").
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ConsentType { get; set; } = string.Empty;

    /// <summary>
    /// Version of the policy/terms the user consented to.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether consent was granted (true) or explicitly denied (false).
    /// </summary>
    public bool IsGranted { get; set; }

    /// <summary>
    /// When consent was recorded.
    /// </summary>
    public DateTimeOffset ConsentedOn { get; set; }

    /// <summary>
    /// IP address at time of consent (for audit purposes).
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent at time of consent (for audit purposes).
    /// </summary>
    [MaxLength(512)]
    public string? UserAgent { get; set; }
}

/// <summary>
/// Standard consent types for GDPR compliance.
/// </summary>
public static class ConsentTypes
{
    public const string PrivacyPolicy = "PrivacyPolicy";
    public const string TermsOfService = "TermsOfService";
    public const string EssentialCookies = "EssentialCookies";
    public const string AnalyticsCookies = "AnalyticsCookies";
    public const string Marketing = "Marketing";
}
