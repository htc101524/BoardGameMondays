namespace BoardGameMondays.Core;

public sealed class BoardGame
{
    public BoardGame(
        string name,
        GameStatus status = GameStatus.Queued,
        Overview? overview = null,
        IEnumerable<Review>? reviews = null,
        string? tagline = null,
        string? imageUrl = null,
        int? minPlayers = null,
        int? maxPlayers = null,
        int? runtimeMinutes = null,
        int? firstPlayRuntimeMinutes = null,
        double? complexity = null,
        double? boardGameGeekScore = null,
        string? boardGameGeekUrl = null,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Status = status;
        Overview = overview ?? EmptyOverview.Instance;
        Reviews = (reviews ?? Array.Empty<Review>()).ToArray();
        Tagline = tagline;
        ImageUrl = imageUrl;

        MinPlayers = minPlayers;
        MaxPlayers = maxPlayers;
        RuntimeMinutes = runtimeMinutes;
        FirstPlayRuntimeMinutes = firstPlayRuntimeMinutes;
        Complexity = complexity;
        BoardGameGeekScore = boardGameGeekScore;
        BoardGameGeekUrl = boardGameGeekUrl;
    }

    public Guid Id { get; }

    public string Name { get; }

    public GameStatus Status { get; }

    public Overview Overview { get; }

    public IReadOnlyList<Review> Reviews { get; }

    public DateTimeOffset? ReviewedOn
        => Reviews.Count == 0 ? null : Reviews.Max(r => r.CreatedOn);

    // Optional metadata for UI.
    public string? Tagline { get; }

    public string? ImageUrl { get; }

    // Optional stats for UI.
    public int? MinPlayers { get; }

    public int? MaxPlayers { get; }

    public int? RuntimeMinutes { get; }

    public int? FirstPlayRuntimeMinutes { get; }

    public double? Complexity { get; }

    public double? BoardGameGeekScore { get; }

    public string? BoardGameGeekUrl { get; }
}

public enum GameStatus
{
    Decided = 0,
    Playing = 1,
    Queued = 2
}
