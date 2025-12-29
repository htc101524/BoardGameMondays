namespace BoardGameMondays.Core;

public sealed class MemberReview : Review
{
    public MemberReview(BgmMember reviewer, int rating, string description, IEnumerable<Comment>? comments = null)
    {
        Reviewer = reviewer;
        Rating = rating;
        Description = description;
        Comments = comments ?? Array.Empty<Comment>();
    }

    public override BgmMember Reviewer { get; }

    public override int Rating { get; }

    public override string Description { get; }

    public override IEnumerable<Comment> Comments { get; }
}
