namespace BoardGameMondays.Core;

public sealed class MemberReview : Review
{
    public MemberReview(
        BgmMember reviewer,
        double rating,
        string description,
        int timesPlayed = 0,
        DateTimeOffset? createdOn = null,
        IEnumerable<Comment>? comments = null,
        Guid? id = null)
    {
        Id = id ?? Guid.Empty;
        Reviewer = reviewer;
        Rating = rating;
        Description = description;
        TimesPlayed = timesPlayed;
        CreatedOn = createdOn ?? DateTimeOffset.UtcNow;
        Comments = comments ?? Array.Empty<Comment>();
    }

    public override Guid Id { get; }

    public override BgmMember Reviewer { get; }

    public override DateTimeOffset CreatedOn { get; }

    public override double Rating { get; }

    public override string Description { get; }

    public override int TimesPlayed { get; }

    public override IEnumerable<Comment> Comments { get; }
}
