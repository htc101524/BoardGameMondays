namespace BoardGameMondays.Core;

public sealed class LatestReviewedGameService
{
    // TODO: Replace with real persistence when reviews are implemented.
    private static readonly LatestReviewedGame[] Seed =
    [
        new(
            Title: "Cascadia",
            Tagline: "A calm, clever tile-laying puzzle with satisfying combos.",
            ImageUrl: "images/placeholder-game-cover.svg",
            ReviewedOn: new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero)
        )
    ];

    public Task<LatestReviewedGame?> GetLatestAsync(CancellationToken ct = default)
        => Task.FromResult(Seed.OrderByDescending(x => x.ReviewedOn).FirstOrDefault());
}
