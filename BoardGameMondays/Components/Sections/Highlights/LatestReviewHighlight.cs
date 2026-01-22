using BoardGameMondays.Core;

namespace BoardGameMondays.Components.Sections.Highlights;

/// <summary>
/// Highlight displaying the latest reviewed board game.
/// </summary>
public sealed class LatestReviewHighlight : IHomeHighlight
{
    private readonly BoardGame? _game;

    public LatestReviewHighlight(BoardGame? latestReviewedGame)
    {
        _game = latestReviewedGame;
    }

    public string Title => "Latest Review";

    public string? Content => _game?.Name;

    public string? Subtitle => _game?.Tagline;

    public string? ImageUrl => _game?.ImageUrl;

    public string? NavigationUrl => _game is not null ? "#thoughts" : null;

    public string AccentColor => "191, 161, 74"; // Gold

    public bool HasData => _game is not null;

    public string EmptyMessage => "No reviews yet. Add your first review!";

    /// <summary>
    /// Gets the underlying board game, if available.
    /// </summary>
    public BoardGame? Game => _game;
}
