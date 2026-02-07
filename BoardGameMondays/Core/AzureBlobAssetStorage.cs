using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

/// <summary>
/// Azure Blob Storage for production deployment.
/// Assets are stored in a single container (configurable, default: "bgm-assets").
/// Paths: avatars/{id}.jpg, games/{guid}.webp, blog/{guid}.png
/// </summary>
public sealed class AzureBlobAssetStorage : IAssetStorage
{
    private readonly StorageOptions.AzureBlobStorageOptions _options;
    private readonly BlobServiceClient _client;

    public StorageProviderMetadata Metadata => new()
    {
        ProviderName = "Azure Blob Storage",
        SupportsListing = true,
        SupportsDelete = true,
        SupportsValidation = true
    };

    public AzureBlobAssetStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value.AzureBlob;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Storage is set to AzureBlob but Storage:AzureBlob:ConnectionString is missing.");
        }

        _client = new BlobServiceClient(_options.ConnectionString);
    }

    public async Task<string> SaveAvatarAsync(Guid memberId, Stream content, string extension, CancellationToken ct = default)
    {
        var blobPath = $"avatars/{memberId}{extension}";
        var url = await UploadAsync(blobPath, content, extension, ct);

        // Cache-buster so browsers refresh after re-upload.
        return AppendCacheBuster(url);
    }

    public async Task<string> SaveGameImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var blobPath = $"games/{fileName}";
        return await UploadAsync(blobPath, content, extension, ct);
    }

    public async Task<string> SaveBlogImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var blobPath = $"blog/{fileName}";
        return await UploadAsync(blobPath, content, extension, ct);
    }

    public async Task<string?> GetImageUrlAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_options.ContainerName);
            var blob = container.GetBlobClient(relativePath);

            // Check if blob exists.
            var exists = await blob.ExistsAsync(ct);
            if (!exists.Value)
                return null;

            return ToPublicUrl(relativePath);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool isValid, string? error)> ValidateImageAsync(string url, CancellationToken ct = default)
    {
        try
        {
            // Extract blob path from URL (remove protocol, container, etc).
            var blobPath = ExtractBlobPathFromUrl(url);
            var container = _client.GetBlobContainerClient(_options.ContainerName);
            var blob = container.GetBlobClient(blobPath);

            // Check existence and ensure it's an image (by size/metadata).
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            if (properties.Value.ContentLength == 0)
                return (false, "Blob is empty");

            // Verify content type is image-like.
            var contentType = properties.Value.ContentType?.ToLowerInvariant() ?? "";
            if (!contentType.StartsWith("image/"))
                return (false, $"Content-Type is not an image: {contentType}");

            return (true, null);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (false, "Blob not found");
        }
        catch (Exception ex)
        {
            return (false, $"Error validating image: {ex.Message}");
        }
    }

    public async Task<IEnumerable<string>> ListImagesAsync(string folder, CancellationToken ct = default)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_options.ContainerName);
            var blobs = new List<string>();

            // Add trailing slash to folder for accurate prefix matching.
            var prefix = folder.TrimEnd('/') + "/";

            await foreach (var blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            {
                // Return relative path (remove prefix).
                var relativePath = blob.Name.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(relativePath))
                    blobs.Add(relativePath);
            }

            return blobs.OrderBy(b => b);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<bool> DeleteImageAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_options.ContainerName);
            var blob = container.GetBlobClient(relativePath);
            var response = await blob.DeleteAsync(Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
            return response.IsError == false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_options.ContainerName);
            var prefix = folder.TrimEnd('/') + "/";
            int deletedCount = 0;

            await foreach (var blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            {
                await container.GetBlobClient(blob.Name).DeleteAsync(cancellationToken: ct);
                deletedCount++;
            }

            return deletedCount;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<string> UploadAsync(string blobPath, Stream content, string extension, CancellationToken ct)
    {
        var container = _client.GetBlobContainerClient(_options.ContainerName);

        if (_options.CreateContainerIfMissing)
        {
            try
            {
                var access = _options.PublicAccessIfCreated ? PublicAccessType.Blob : PublicAccessType.None;
                await container.CreateIfNotExistsAsync(publicAccessType: access, cancellationToken: ct);
            }
            catch (RequestFailedException)
            {
                // If we lack permission to create containers, uploads may still work if it already exists.
            }
        }

        var blob = container.GetBlobClient(blobPath);
        var headers = new BlobHttpHeaders { ContentType = GuessContentType(extension) };

        await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = headers }, ct);

        return ToPublicUrl(blobPath);
    }

    private string ToPublicUrl(string blobPath)
    {
        var baseUrl = _options.BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"{baseUrl.TrimEnd('/')}/{_options.ContainerName}/{blobPath}";
        }

        // Default to the storage account URL.
        var accountUrl = _client.Uri.ToString().TrimEnd('/');
        return $"{accountUrl}/{_options.ContainerName}/{blobPath}";
    }

    private string ExtractBlobPathFromUrl(string url)
    {
        // URL format: https://account.blob.core.windows.net/container/blob-path
        // or: https://cdn.example.com/container/blob-path
        var parts = url.TrimEnd('?').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var containerIndex = Array.IndexOf(parts, _options.ContainerName);
        if (containerIndex >= 0 && containerIndex < parts.Length - 1)
        {
            var blobParts = parts.Skip(containerIndex + 1);
            return string.Join("/", blobParts);
        }

        // Fallback: assume last parts are the blob path.
        return parts.Length > 2 ? string.Join("/", parts.Skip(parts.Length - 2)) : url;
    }

    private static string AppendCacheBuster(string url)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}v={DateTimeOffset.UtcNow.UtcTicks}";
    }

    private static string GuessContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
