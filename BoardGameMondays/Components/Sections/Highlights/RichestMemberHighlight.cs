namespace BoardGameMondays.Components.Sections.Highlights;

/// <summary>
/// Highlight displaying the member with the most BGM coins.
/// </summary>
public sealed class RichestMemberHighlight : IHomeHighlight
{
    private readonly string? _displayName;
    private readonly int _coins;
    private readonly string? _userId;

    public RichestMemberHighlight(string? displayName, int coins, string? userId = null)
    {
        _displayName = displayName;
        _coins = coins;
        _userId = userId;
    }

    public string Title => "Richest BGM Member";

    public string? Content => _displayName;

    public string? Subtitle => HasData ? $"{_coins:N0} BGM Coins" : null;

    public string? ImageUrl => null;

    public string? NavigationUrl => "/leaderboard";

    public string AccentColor => "255, 193, 7"; // Coin gold

    public bool HasData => !string.IsNullOrWhiteSpace(_displayName);

    public string EmptyMessage => "No coin data yet.";

    /// <summary>
    /// Gets the user ID of the richest member, if available.
    /// </summary>
    public string? UserId => _userId;

    /// <summary>
    /// Gets the coin count.
    /// </summary>
    public int Coins => _coins;
}
