using Markdig;
using Microsoft.AspNetCore.Components;

namespace BoardGameMondays.Core;

/// <summary>
/// Renders Markdown to HTML using Markdig.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    /// <summary>
    /// Converts Markdown text to HTML.
    /// </summary>
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(markdown, Pipeline);
    }

    /// <summary>
    /// Converts Markdown text to a MarkupString for rendering in Blazor.
    /// </summary>
    public static MarkupString ToMarkupString(string? markdown)
    {
        return new MarkupString(ToHtml(markdown));
    }
}
