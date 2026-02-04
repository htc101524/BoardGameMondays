using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

public sealed class AzureBlobAssetStorage : IAssetStorage
{
    private readonly StorageOptions.AzureBlobStorageOptions _options;
    private readonly BlobServiceClient _client;

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
