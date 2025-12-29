namespace BoardGameMondays.Core;

public sealed class BoardGame
{
    public BoardGame(
        string name,
        Overview? overview = null,
        IEnumerable<Review>? reviews = null,
        DateTimeOffset? reviewedOn = null,
        string? tagline = null,
        string? imageUrl = null)
    {
        Name = name;
        Overview = overview ?? EmptyOverview.Instance;
        Reviews = (reviews ?? Array.Empty<Review>()).ToArray();
        ReviewedOn = reviewedOn;
        Tagline = tagline;
        ImageUrl = imageUrl;
    }

    public string Name { get; }

    public Overview Overview { get; }

    public IReadOnlyList<Review> Reviews { get; }

    public DateTimeOffset? ReviewedOn { get; }

    // Optional metadata for UI.
    public string? Tagline { get; }

    public string? ImageUrl { get; }
}
