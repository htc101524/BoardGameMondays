namespace BoardGameMondays.Core;

public sealed record LatestReviewedGame(
    string Title,
    string Tagline,
    string ImageUrl,
    DateTimeOffset ReviewedOn
);
