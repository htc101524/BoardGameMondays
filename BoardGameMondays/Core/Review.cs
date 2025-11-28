namespace BoardGameMondays.Core;

public abstract class Review
{
    public abstract BgmMember Reviewer { get; }

    public virtual IEnumerable<Comment> Comments { get; }

    public abstract int Rating { get; }
}