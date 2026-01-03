namespace BoardGameMondays.Core;

public abstract class Review
{
    public abstract BgmMember Reviewer { get; }

    public virtual IEnumerable<Comment> Comments { get; } = Array.Empty<Comment>();

    public abstract DateTimeOffset CreatedOn { get; }

    public abstract string Description { get; }

    public abstract double Rating { get; }
}