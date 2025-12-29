namespace BoardGameMondays.Core;

public sealed class BoardGameService
{
    // TODO: Replace with real persistence when reviews are implemented.
    private static readonly BoardGame[] Seed =
    [
        CreateCascadia()
    ];

    private static BoardGame CreateCascadia()
    {
        var henry = new DemoBgmMember("Henry");
        var alex = new DemoBgmMember("Alex");
        var sam = new DemoBgmMember("Sam");

        return new BoardGame(
            name: "Cascadia",
            tagline: "A calm, clever tile-laying puzzle with satisfying combos.",
            imageUrl: "images/placeholder-game-cover.svg",
            reviewedOn: new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero),
            reviews:
            [
                new MemberReview(
                    reviewer: henry,
                    rating: 9,
                    description: "Soothing, quick to teach, and the scoring goals keep it fresh. I love how the drafting stays gentle but still forces real trade-offs."
                ),
                new MemberReview(
                    reviewer: alex,
                    rating: 8,
                    description: "Great puzzle feel. The spatial constraints are satisfying and it never feels mean. I’d like a bit more tension, but it’s a great weeknight game."
                ),
                new MemberReview(
                    reviewer: sam,
                    rating: 7,
                    description: "Solid and relaxing. I enjoy the combos, but it can feel a touch samey if you play it back-to-back. Still a keeper."
                )
            ]
        );
    }

    public Task<BoardGame?> GetLatestReviewedAsync(CancellationToken ct = default)
    {
        // In-memory seed; no async work yet.
        var latest = Seed
            .OrderByDescending(x => x.ReviewedOn ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return Task.FromResult<BoardGame?>(latest);
    }
}
