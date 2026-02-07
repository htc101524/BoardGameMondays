using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BoardGameMondays.Core;

/// <summary>
/// Abstraction for asset storage (images, avatars, etc.) that works across multiple providers
/// (Local filesystem, Azure Blob, S3, GCS, etc.).
/// This interface enables provider-agnostic image management and supports migration between backends.
/// </summary>
public interface IAssetStorage
{
    /// <summary>
    /// Metadata about this storage provider (name, capabilities, etc.)
    /// </summary>
    StorageProviderMetadata Metadata { get; }

    /// <summary>
    /// Save a member avatar. Overwrites previous avatar for same member (deterministic path).
    /// </summary>
    Task<string> SaveAvatarAsync(Guid memberId, Stream content, string extension, CancellationToken ct = default);

    /// <summary>
    /// Save a game cover image. Each upload gets unique GUID name.
    /// </summary>
    Task<string> SaveGameImageAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>
    /// Save a blog post image. Each upload gets unique GUID name.
    /// </summary>
    Task<string> SaveBlogImageAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>
    /// Retrieve the full public URL for an image already stored.
    /// Used to verify or re-generate URLs from database references.
    /// </summary>
    Task<string?> GetImageUrlAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Validate that an image URL is accessible and contains valid image data.
    /// Returns (isValid, errorMessage).
    /// </summary>
    Task<(bool isValid, string? error)> ValidateImageAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// List all relative paths (not full URLs) of images in a given folder.
    /// Example: folder="avatars" → ["{memberId}.jpg", "{memberId}.png", ...]
    /// Example: folder="games" → ["abc123def.webp", "xyz789abc.jpg", ...]
    /// Returns empty list if folder doesn't exist.
    /// </summary>
    Task<IEnumerable<string>> ListImagesAsync(string folder, CancellationToken ct = default);

    /// <summary>
    /// Delete an image by its relative path.
    /// Returns true if deleted, false if not found (not an error).
    /// </summary>
    Task<bool> DeleteImageAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Delete all images in a folder. Returns count deleted.
    /// </summary>
    Task<int> DeleteFolderAsync(string folder, CancellationToken ct = default);
}

/// <summary>
/// Metadata about a storage provider for introspection (e.g., capabilities, name).
/// Used to determine which features/operations are supported before attempting them.
/// </summary>
public class StorageProviderMetadata
{
    /// <summary>
    /// Display name of provider (e.g., "Local Filesystem", "Azure Blob Storage", "S3")
    /// </summary>
    public required string ProviderName { get; set; }

    /// <summary>
    /// If true, this provider supports listing images (e.g., enumerating all files in a folder).
    /// </summary>
    public bool SupportsListing { get; set; } = true;

    /// <summary>
    /// If true, this provider supports deleting images (cleanup/garbage collection).
    /// </summary>
    public bool SupportsDelete { get; set; } = true;

    /// <summary>
    /// If true, this provider supports validation (checking if image is accessible).
    /// </summary>
    public bool SupportsValidation { get; set; } = true;
}
