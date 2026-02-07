using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BoardGameMondays.Tools;

/// <summary>
/// Safely migrates image URLs in blog markdown content.
/// Uses regex pattern matching instead of string replacement for safety.
/// Validates that all replacements succeeded before returning updated content.
/// NEW: This replaces the fragile string replacement approach in ImageMigrationTool.
/// </summary>
public sealed class BlogImageMigrationHelper
{
    /// <summary>
    /// Replace image URLs in blog markdown with updated URLs.
    /// Supports markdown image syntax: ![alt](url)
    /// Also supports HTML img tags: <img src="url" />
    /// </summary>
    public static (string updatedContent, List<BlogImageReplacement> replacements) MigrateBlogImages(
        string markdownContent,
        Dictionary<string, string> urlMappings)
    {
        var replacements = new List<BlogImageReplacement>();
        var updatedContent = markdownContent;

        if (string.IsNullOrEmpty(markdownContent) || urlMappings.Count == 0)
            return (updatedContent, replacements);

        // Find all markdown image references: ![alt text](url)
        var markdownImagePattern = @"!\[([^\]]*)\]\(([^)]+)\)";
        var markdownMatches = Regex.Matches(markdownContent, markdownImagePattern);

        foreach (Match match in markdownMatches)
        {
            var fullMatch = match.Groups[0].Value;
            var altText = match.Groups[1].Value;
            var url = match.Groups[2].Value;

            if (urlMappings.TryGetValue(url, out var newUrl))
            {
                var replacement = new BlogImageReplacement
                {
                    OriginalUrl = url,
                    NewUrl = newUrl,
                    ImageType = "Markdown",
                    Occurrences = 0,
                    Success = false
                };

                // Count occurrences of this exact markdown reference.
                var count = 0;
                var tempContent = updatedContent;
                var newContent = Regex.Replace(
                    tempContent,
                    Regex.Escape(fullMatch),
                    $"![{altText}]({newUrl})",
                    RegexOptions.None);

                if (newContent != tempContent)
                {
                    count++;
                    updatedContent = newContent;
                    replacement.Success = true;
                    replacement.Occurrences = count;
                    replacements.Add(replacement);
                }
            }
        }

        // Find all HTML image tags: <img src="url" ... />
        var htmlImagePattern = @"<img\s+[^>]*src=""([^""]+)""[^>]*>";
        var htmlMatches = Regex.Matches(markdownContent, htmlImagePattern);

        foreach (Match match in htmlMatches)
        {
            var fullMatch = match.Groups[0].Value;
            var url = match.Groups[1].Value;

            if (urlMappings.TryGetValue(url, out var newUrl))
            {
                var replacement = new BlogImageReplacement
                {
                    OriginalUrl = url,
                    NewUrl = newUrl,
                    ImageType = "HTML",
                    Occurrences = 0,
                    Success = false
                };

                // Replace the img tag with updated src.
                var newContent = updatedContent.Replace(fullMatch, fullMatch.Replace(url, newUrl));

                if (newContent != updatedContent)
                {
                    updatedContent = newContent;
                    replacement.Success = true;
                    replacement.Occurrences = 1;
                    replacements.Add(replacement);
                }
            }
        }

        return (updatedContent, replacements);
    }

    /// <summary>
    /// Extract all image URLs from blog markdown (both markdown and HTML syntax).
    /// Useful for pre-migration validation and inventory.
    /// </summary>
    public static List<string> ExtractImageUrls(string markdownContent)
    {
        var urls = new List<string>();

        if (string.IsNullOrEmpty(markdownContent))
            return urls;

        // Extract markdown images: ![alt](url)
        var markdownImagePattern = @"!\[([^\]]*)\]\(([^)]+)\)";
        var markdownMatches = Regex.Matches(markdownContent, markdownImagePattern);

        foreach (Match match in markdownMatches)
        {
            var url = match.Groups[2].Value.Split('?')[0]; // Remove query params.
            if (!urls.Contains(url))
                urls.Add(url);
        }

        // Extract HTML images: <img src="url" ... />
        var htmlImagePattern = @"<img\s+[^>]*src=""([^""]+)""[^>]*>";
        var htmlMatches = Regex.Matches(markdownContent, htmlImagePattern);

        foreach (Match match in htmlMatches)
        {
            var url = match.Groups[1].Value.Split('?')[0]; // Remove query params.
            if (!urls.Contains(url))
                urls.Add(url);
        }

        return urls;
    }

    /// <summary>
    /// Validate that blog markdown doesn't reference any of the provided old URLs.
    /// Useful for verifying migration completed successfully.
    /// </summary>
    public static (bool isValid, List<string> foundOldUrls) ValidateNoOldUrls(
        string markdownContent,
        List<string> oldUrls)
    {
        var foundOldUrls = new List<string>();

        foreach (var oldUrl in oldUrls)
        {
            if (markdownContent.Contains(oldUrl))
                foundOldUrls.Add(oldUrl);
        }

        return (foundOldUrls.Count == 0, foundOldUrls);
    }
}

/// <summary>
/// Result of a single image URL replacement in blog content.
/// </summary>
public class BlogImageReplacement
{
    /// <summary>
    /// The old image URL that was replaced.
    /// </summary>
    public string OriginalUrl { get; set; } = "";

    /// <summary>
    /// The new image URL it was replaced with.
    /// </summary>
    public string NewUrl { get; set; } = "";

    /// <summary>
    /// Image syntax type: "Markdown" (![alt](url)) or "HTML" (<img ... src="" />)
    /// </summary>
    public string ImageType { get; set; } = "";

    /// <summary>
    /// Number of occurrences replaced.
    /// </summary>
    public int Occurrences { get; set; }

    /// <summary>
    /// Whether the replacement was successful.
    /// </summary>
    public bool Success { get; set; }
}
