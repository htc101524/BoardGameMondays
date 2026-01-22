namespace BoardGameMondays.Components.Sections.Highlights;

/// <summary>
/// Represents a highlight item displayed on the homepage sidebar.
/// Implementations provide data for individual highlight cards.
/// </summary>
public interface IHomeHighlight
{
    /// <summary>
    /// Gets the display title for the highlight card header (e.g., "Latest Review", "Richest BGM Member").
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the main content text to display in the highlight card.
    /// </summary>
    string? Content { get; }

    /// <summary>
    /// Gets a secondary/subtitle text to display below the main content.
    /// </summary>
    string? Subtitle { get; }

    /// <summary>
    /// Gets an optional image URL to display in the highlight card.
    /// </summary>
    string? ImageUrl { get; }

    /// <summary>
    /// Gets the navigation URL when the highlight is clicked. Null if not clickable.
    /// </summary>
    string? NavigationUrl { get; }

    /// <summary>
    /// Gets the CSS accent color variable for theming (e.g., "191, 161, 74" for gold).
    /// </summary>
    string AccentColor { get; }

    /// <summary>
    /// Gets whether there is data to display. If false, a placeholder message is shown.
    /// </summary>
    bool HasData { get; }

    /// <summary>
    /// Gets the message to display when there is no data.
    /// </summary>
    string EmptyMessage { get; }
}
