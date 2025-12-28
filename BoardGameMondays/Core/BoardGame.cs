namespace BoardGameMondays.Core;

public abstract class BoardGame
{
    public abstract string Name { get; }

    public abstract Overview Overview { get; }

    public abstract IEnumerable<Review> Reviews { get; }

    // Optional metadata for UI.
    public virtual string? Tagline => null;

    public virtual string? ImageUrl => null;
}
