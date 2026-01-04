namespace BoardGameMondays.Data.Entities;

public sealed class ReviewAgreementEntity
{
    public int Id { get; set; }

    public Guid ReviewId { get; set; }

    public ReviewEntity Review { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;

    // 1..5
    public int Score { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
}
