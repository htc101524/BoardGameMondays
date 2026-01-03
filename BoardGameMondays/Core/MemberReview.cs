namespace BoardGameMondays.Core;

public sealed class MemberReview : Review
{
    public MemberReview(
        BgmMember reviewer,
        double rating,
        string description,
        DateTimeOffset? createdOn = null,
        IEnumerable<Comment>? comments = null)
    {
        Reviewer = reviewer;
        Rating = rating;
        Description = description;
        CreatedOn = createdOn ?? DateTimeOffset.UtcNow;
        Comments = comments ?? Array.Empty<Comment>();
    }

    public override BgmMember Reviewer { get; }

    public override DateTimeOffset CreatedOn { get; }

    public override double Rating { get; }

    public override string Description { get; }

    public override IEnumerable<Comment> Comments { get; }
}
