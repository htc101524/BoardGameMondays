using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

/// <summary>
/// Tracks GDPR "right to be forgotten" deletion requests.
/// </summary>
public sealed class DataDeletionRequestEntity
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// The user requesting deletion.
    /// </summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Email address at time of request (preserved for audit).
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// When the deletion was requested.
    /// </summary>
    public DateTimeOffset RequestedOn { get; set; }

    /// <summary>
    /// When the deletion will be/was executed (typically 30 days after request).
    /// </summary>
    public DateTimeOffset ScheduledDeletionOn { get; set; }

    /// <summary>
    /// When the deletion was actually completed. Null if pending.
    /// </summary>
    public DateTimeOffset? CompletedOn { get; set; }

    /// <summary>
    /// Status of the deletion request.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = DeletionStatus.Pending;

    /// <summary>
    /// Optional reason provided by user for deletion.
    /// </summary>
    [MaxLength(1024)]
    public string? Reason { get; set; }

    /// <summary>
    /// If cancelled, when was it cancelled.
    /// </summary>
    public DateTimeOffset? CancelledOn { get; set; }
}

/// <summary>
/// Status values for deletion requests.
/// </summary>
public static class DeletionStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}
