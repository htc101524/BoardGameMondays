using BoardGameMondays.Core;

namespace BoardGameMondays.Components.Sections.Highlights;

/// <summary>
/// Highlight displaying the latest blog post.
/// </summary>
public sealed class LatestBlogPostHighlight : IHomeHighlight
{
    private readonly BlogService.BlogPost? _post;

    public LatestBlogPostHighlight(BlogService.BlogPost? latestPost)
    {
        _post = latestPost;
    }

    public string Title => "Latest Blog Post";

    public string? Content => _post?.Title;

    public string? Subtitle => _post is not null
        ? FormatRelativeDate(_post.CreatedOn)
        : null;

    public string? ImageUrl => null;

    public string? NavigationUrl => _post is not null && !string.IsNullOrWhiteSpace(_post.Slug)
        ? $"/blog/{_post.Slug}"
        : "/blog";

    public string AccentColor => "81, 118, 108"; // Teal/green

    public bool HasData => _post is not null;

    public string EmptyMessage => "No blog posts yet.";

    /// <summary>
    /// Gets the underlying blog post, if available.
    /// </summary>
    public BlogService.BlogPost? Post => _post;

    private static string FormatRelativeDate(DateTimeOffset date)
    {
        var now = DateTimeOffset.UtcNow;
        var diff = now - date;

        if (diff.TotalMinutes < 1)
            return "Just now";
        if (diff.TotalHours < 1)
            return $"{(int)diff.TotalMinutes} minute{((int)diff.TotalMinutes == 1 ? "" : "s")} ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours} hour{((int)diff.TotalHours == 1 ? "" : "s")} ago";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays} day{((int)diff.TotalDays == 1 ? "" : "s")} ago";
        if (diff.TotalDays < 30)
        {
            var weeks = (int)(diff.TotalDays / 7);
            return $"{weeks} week{(weeks == 1 ? "" : "s")} ago";
        }

        return date.ToString("MMM d, yyyy");
    }
}
