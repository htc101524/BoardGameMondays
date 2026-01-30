namespace BoardGameMondays.Core;

public sealed class BoardGame
{
    public BoardGame(
        string name,
        GameStatus status = GameStatus.Queued,
        Overview? overview = null,
        IEnumerable<Review>? reviews = null,
        IEnumerable<VictoryRoute>? victoryRoutes = null,
        string? tagline = null,
        string? imageUrl = null,
        int? minPlayers = null,
        int? maxPlayers = null,
        int? runtimeMinutes = null,
        int? firstPlayRuntimeMinutes = null,
        double? complexity = null,
        double? boardGameGeekScore = null,
        string? boardGameGeekUrl = null,
        bool areScoresCountable = false,
        int? highScore = null,
        Guid? highScoreMemberId = null,
        string? highScoreMemberName = null,
        DateTimeOffset? highScoreAchievedOn = null,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Status = status;
        Overview = overview ?? EmptyOverview.Instance;
        Reviews = (reviews ?? Array.Empty<Review>()).ToArray();
        VictoryRoutes = (victoryRoutes ?? Array.Empty<VictoryRoute>()).ToArray();
        Tagline = tagline;
        ImageUrl = imageUrl;

        MinPlayers = minPlayers;
        MaxPlayers = maxPlayers;
        RuntimeMinutes = runtimeMinutes;
        FirstPlayRuntimeMinutes = firstPlayRuntimeMinutes;
        Complexity = complexity;
        BoardGameGeekScore = boardGameGeekScore;
        BoardGameGeekUrl = boardGameGeekUrl;
        AreScoresCountable = areScoresCountable;
        HighScore = highScore;
        HighScoreMemberId = highScoreMemberId;
        HighScoreMemberName = highScoreMemberName;
        HighScoreAchievedOn = highScoreAchievedOn;
    }

    public Guid Id { get; }

    public string Name { get; }

    public GameStatus Status { get; }

    public Overview Overview { get; }

    public IReadOnlyList<Review> Reviews { get; }

    public IReadOnlyList<VictoryRoute> VictoryRoutes { get; }

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

    public bool AreScoresCountable { get; }

    public int? HighScore { get; }

    public Guid? HighScoreMemberId { get; }

    public string? HighScoreMemberName { get; }

    public DateTimeOffset? HighScoreAchievedOn { get; }
}

public sealed record VictoryRoute(Guid Id, string Name, VictoryRouteType Type, bool IsRequired, int SortOrder, IReadOnlyList<VictoryRouteOption> Options);

public sealed record VictoryRouteOption(Guid Id, string Value, int SortOrder);

public enum VictoryRouteType
{
    Dropdown = 0,
    Checkbox = 1
}

public enum GameStatus
{
    Decided = 0,
    Playing = 1,
    Queued = 2
}
