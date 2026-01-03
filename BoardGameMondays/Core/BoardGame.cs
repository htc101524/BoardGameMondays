namespace BoardGameMondays.Core;

public sealed class BoardGame
{
    public BoardGame(
        string name,
        GameStatus status = GameStatus.Queued,
        Overview? overview = null,
        IEnumerable<Review>? reviews = null,
        DateTimeOffset? reviewedOn = null,
        string? tagline = null,
        string? imageUrl = null,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Status = status;
        Overview = overview ?? EmptyOverview.Instance;
        Reviews = (reviews ?? Array.Empty<Review>()).ToArray();
        ReviewedOn = reviewedOn;
        Tagline = tagline;
        ImageUrl = imageUrl;
    }

    public Guid Id { get; }

    public string Name { get; }

    public GameStatus Status { get; }

    public Overview Overview { get; }

    public IReadOnlyList<Review> Reviews { get; }

    public DateTimeOffset? ReviewedOn { get; }

    // Optional metadata for UI.
    public string? Tagline { get; }

    public string? ImageUrl { get; }
}

public enum GameStatus
{
    Decided = 0,
    Playing = 1,
    Queued = 2
}
