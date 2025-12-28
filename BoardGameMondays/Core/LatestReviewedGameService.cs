namespace BoardGameMondays.Core;

public sealed class LatestReviewedGameService
{
    // TODO: Replace with real persistence when reviews are implemented.
    private static readonly LatestReviewedGame[] Seed =
    [
        new(
            title: "Cascadia",
            tagline: "A calm, clever tile-laying puzzle with satisfying combos.",
            imageUrl: "images/placeholder-game-cover.svg",
            whatWeThought: "A relaxing puzzle that still rewards planning. The spatial constraints create a steady stream of satisfying small decisions, and the scoring goals push you to balance short-term points with long-term positioning.",
            reviewedOn: new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero)
        )
    ];

    public Task<BoardGame?> GetLatestAsync(CancellationToken ct = default)
        => Task.FromResult<BoardGame?>(Seed.OrderByDescending(x => x.ReviewedOn).FirstOrDefault());
}
