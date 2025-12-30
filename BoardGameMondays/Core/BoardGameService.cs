namespace BoardGameMondays.Core;

public sealed class BoardGameService
{
    // TODO: Replace with real persistence when a database is introduced.
    // For now, treat these as the "logged to the site" games.
    private static readonly IReadOnlyList<BoardGame> DecidedSeed =
    [
        CreateCascadia(),
        new BoardGame(
            name: "Azul",
            tagline: "Draft tiles, build patterns, and try not to take what you can’t place.",
            imageUrl: "images/placeholder-game-cover.svg"),
        new BoardGame(
            name: "Wingspan",
            tagline: "A cozy engine-builder about birds with surprisingly crunchy decisions.",
            imageUrl: "images/placeholder-game-cover.svg")
    ];

    private static readonly IReadOnlyList<BoardGame> PlayingSeed =
    [
        new BoardGame(
            name: "The Crew: Mission Deep Sea",
            tagline: "A co-op trick-taking campaign with clever communication limits.",
            imageUrl: "images/placeholder-game-cover.svg"),
        new BoardGame(
            name: "Spirit Island",
            tagline: "Tense co-op defense with powerful combos and lots to master.",
            imageUrl: "images/placeholder-game-cover.svg")
    ];

    private static readonly IReadOnlyList<BoardGame> QueuedSeed =
    [
        new BoardGame(
            name: "Heat: Pedal to the Metal",
            tagline: "Push-your-luck racing with clean rules and exciting turns.",
            imageUrl: "images/placeholder-game-cover.svg"),
        new BoardGame(
            name: "Dune: Imperium",
            tagline: "Deck-building plus worker placement in a tight, tactical package.",
            imageUrl: "images/placeholder-game-cover.svg")
    ];

    private static IReadOnlyList<BoardGame> AllLoggedGames =>
        DecidedSeed.Concat(PlayingSeed).Concat(QueuedSeed).ToArray();

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
        var latest = AllLoggedGames
            .OrderByDescending(x => x.ReviewedOn ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return Task.FromResult<BoardGame?>(latest);
    }

    public Task<IReadOnlyList<BoardGame>> GetDecidedAsync(CancellationToken ct = default)
        => Task.FromResult(DecidedSeed);

    public Task<IReadOnlyList<BoardGame>> GetPlayingAsync(CancellationToken ct = default)
        => Task.FromResult(PlayingSeed);

    public Task<IReadOnlyList<BoardGame>> GetQueuedAsync(CancellationToken ct = default)
        => Task.FromResult(QueuedSeed);
}
