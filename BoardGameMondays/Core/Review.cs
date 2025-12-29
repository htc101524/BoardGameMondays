namespace BoardGameMondays.Core;

public abstract class Review
{
    public abstract BgmMember Reviewer { get; }

    public virtual IEnumerable<Comment> Comments { get; } = Array.Empty<Comment>();

    public abstract string Description { get; }

    public abstract int Rating { get; }
}