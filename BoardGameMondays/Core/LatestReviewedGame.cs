namespace BoardGameMondays.Core;

public sealed class LatestReviewedGame : BoardGame
{
    public LatestReviewedGame(
        string title,
        string tagline,
        string imageUrl,
        DateTimeOffset reviewedOn)
    {
        Title = title;
        Tagline = tagline;
        ImageUrl = imageUrl;
        ReviewedOn = reviewedOn;
    }

    public string Title { get; }
    public override string Name => Title;

    public override string Tagline { get; }

    public override string ImageUrl { get; }

    public DateTimeOffset ReviewedOn { get; }

    public override Overview Overview => EmptyOverview.Instance;

    public override IEnumerable<Review> Reviews => Array.Empty<Review>();
}
