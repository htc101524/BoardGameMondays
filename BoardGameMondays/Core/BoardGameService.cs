namespace BoardGameMondays.Core;

public sealed class BoardGameService
{
    private readonly List<BoardGame> _games;
    private Guid? _featuredGameId;

    public event Action? Changed;

    public BoardGameService()
    {
        // TODO: Replace with real persistence when a database is introduced.
        // For now, treat these as the "logged to the site" games.
        _games =
        [
            CreateCascadia(),
            new BoardGame(
                name: "Azul",
                status: GameStatus.Decided,
                tagline: "Draft tiles, build patterns, and try not to take what you can’t place.",
                imageUrl: "images/placeholder-game-cover.svg"),
            new BoardGame(
                name: "Wingspan",
                status: GameStatus.Decided,
                tagline: "A cozy engine-builder about birds with surprisingly crunchy decisions.",
                imageUrl: "images/placeholder-game-cover.svg"),
            new BoardGame(
                name: "The Crew: Mission Deep Sea",
                status: GameStatus.Playing,
                tagline: "A co-op trick-taking campaign with clever communication limits.",
                imageUrl: "images/placeholder-game-cover.svg"),
            new BoardGame(
                name: "Spirit Island",
                status: GameStatus.Playing,
                tagline: "Tense co-op defense with powerful combos and lots to master.",
                imageUrl: "images/placeholder-game-cover.svg"),
            new BoardGame(
                name: "Heat: Pedal to the Metal",
                status: GameStatus.Queued,
                tagline: "Push-your-luck racing with clean rules and exciting turns.",
                imageUrl: "images/placeholder-game-cover.svg"),
            new BoardGame(
                name: "Dune: Imperium",
                status: GameStatus.Queued,
                tagline: "Deck-building plus worker placement in a tight, tactical package.",
                imageUrl: "images/placeholder-game-cover.svg")
        ];
    }

    private static BoardGame CreateCascadia()
    {
        var henry = new DemoBgmMember("Henry");
        var alex = new DemoBgmMember("Alex");
        var sam = new DemoBgmMember("Sam");

        return new BoardGame(
            name: "Cascadia",
            status: GameStatus.Decided,
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
        // In-memory store; no async work yet.
        var latest = _games
            .OrderByDescending(x => x.ReviewedOn ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return Task.FromResult<BoardGame?>(latest);
    }

    public Task<BoardGame?> GetFeaturedOrLatestReviewedAsync(CancellationToken ct = default)
    {
        if (_featuredGameId is { } featuredId)
        {
            var featured = _games.FirstOrDefault(g => g.Id == featuredId);
            if (featured is not null)
            {
                return Task.FromResult<BoardGame?>(featured);
            }

            _featuredGameId = null;
        }

        return GetLatestReviewedAsync(ct);
    }

    public Task SetFeaturedGameAsync(Guid? gameId, CancellationToken ct = default)
    {
        _featuredGameId = gameId;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task<Guid?> GetFeaturedGameIdAsync(CancellationToken ct = default)
        => Task.FromResult(_featuredGameId);

    public Task<IReadOnlyList<BoardGame>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BoardGame>>(
            _games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToArray());

    public Task<BoardGame?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_games.FirstOrDefault(g => g.Id == id));

    public Task<IReadOnlyList<BoardGame>> GetByStatusAsync(GameStatus status, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BoardGame>>(
            _games.Where(g => g.Status == status)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());

    public Task<BoardGame> AddGameAsync(
        string name,
        GameStatus status,
        string? tagline = null,
        string? imageUrl = null,
        DateTimeOffset? reviewedOn = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var game = new BoardGame(
            name: name.Trim(),
            status: status,
            tagline: tagline,
            imageUrl: imageUrl,
            reviewedOn: reviewedOn);

        _games.Add(game);
        Changed?.Invoke();
        return Task.FromResult(game);
    }

    public Task<BoardGame?> UpdateGameAsync(
        Guid id,
        string name,
        GameStatus status,
        string? tagline,
        string? imageUrl,
        DateTimeOffset? reviewedOn,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var existingIndex = _games.FindIndex(g => g.Id == id);
        if (existingIndex < 0)
        {
            return Task.FromResult<BoardGame?>(null);
        }

        var existing = _games[existingIndex];

        var updated = new BoardGame(
            id: existing.Id,
            name: name.Trim(),
            status: status,
            overview: existing.Overview,
            reviews: existing.Reviews,
            reviewedOn: reviewedOn,
            tagline: tagline,
            imageUrl: imageUrl);

        _games[existingIndex] = updated;
        Changed?.Invoke();
        return Task.FromResult<BoardGame?>(updated);
    }

    public Task<BoardGame?> AddReviewAsync(Guid gameId, Review review, CancellationToken ct = default)
    {
        if (review is null)
        {
            throw new ArgumentNullException(nameof(review));
        }

        var existingIndex = _games.FindIndex(g => g.Id == gameId);
        if (existingIndex < 0)
        {
            return Task.FromResult<BoardGame?>(null);
        }

        var existing = _games[existingIndex];
        var updatedReviews = existing.Reviews.Concat(new[] { review }).ToArray();

        var updated = new BoardGame(
            id: existing.Id,
            name: existing.Name,
            status: existing.Status,
            overview: existing.Overview,
            reviews: updatedReviews,
            reviewedOn: existing.ReviewedOn,
            tagline: existing.Tagline,
            imageUrl: existing.ImageUrl);

        _games[existingIndex] = updated;
        Changed?.Invoke();
        return Task.FromResult<BoardGame?>(updated);
    }
}
