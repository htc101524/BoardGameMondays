using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

/// <summary>
/// Local filesystem storage for development and on-premises deployment.
/// Assets are stored in a folder structure: uploads/avatars/, images/games/, uploads/blog/
/// In production on Azure App Service, uses %HOME%/bgm-assets (persistent across redeploys).
/// </summary>
public sealed class LocalAssetStorage : IAssetStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly StorageOptions _options;

    public StorageProviderMetadata Metadata => new()
    {
        ProviderName = "Local Filesystem",
        SupportsListing = true,
        SupportsDelete = true,
        SupportsValidation = true
    };

    public LocalAssetStorage(IWebHostEnvironment env, IOptions<StorageOptions> options)
    {
        _env = env;
        _options = options.Value;
    }

    private string ResolveAssetsRoot()
    {
        var configured = _options.Local.RootPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        // On Azure App Service, %HOME% (Windows) or $HOME (Linux) is persistent across deployments.
        if (!_env.IsDevelopment())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.Combine(home, "bgm-assets");
            }
        }

        // Dev/default: keep the assets in wwwroot so they're served by default static file middleware.
        return _env.WebRootPath;
    }

    public async Task<string> SaveAvatarAsync(Guid memberId, Stream content, string extension, CancellationToken ct = default)
    {
        var uploadsRoot = Path.Combine(ResolveAssetsRoot(), "uploads", "avatars");
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"{memberId}{extension}";
        var filePath = Path.Combine(uploadsRoot, fileName);

        await using (var outStream = File.Create(filePath))
        {
            await content.CopyToAsync(outStream, ct);
        }

        // Cache-buster so browsers refresh after re-upload.
        return $"/uploads/avatars/{fileName}?v={DateTimeOffset.UtcNow.UtcTicks}";
    }

    public async Task<string> SaveGameImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var absolutePath = Path.Combine(ResolveAssetsRoot(), "images", "games", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var outStream = File.Create(absolutePath))
        {
            await content.CopyToAsync(outStream, ct);
        }

        return $"/images/games/{fileName}";
    }

    public async Task<string> SaveBlogImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var absolutePath = Path.Combine(ResolveAssetsRoot(), "uploads", "blog", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var outStream = File.Create(absolutePath))
        {
            await content.CopyToAsync(outStream, ct);
        }

        return $"/uploads/blog/{fileName}";
    }

    public Task<string?> GetImageUrlAsync(string relativePath, CancellationToken ct = default)
    {
        // For local storage, the URL is just the relative path.
        // If the file doesn't exist, return null.
        var fullPath = Path.Combine(ResolveAssetsRoot(), relativePath.TrimStart('/'));
        var result = File.Exists(fullPath) ? $"/{relativePath.TrimStart('/')}" : null;
        return Task.FromResult(result);
    }

    public async Task<(bool isValid, string? error)> ValidateImageAsync(string url, CancellationToken ct = default)
    {
        try
        {
            // For local storage, strip leading slash and check file exists and is readable.
            var relativePath = url.TrimStart('/').Split('?')[0]; // Remove query params
            var fullPath = Path.Combine(ResolveAssetsRoot(), relativePath);

            if (!File.Exists(fullPath))
                return (false, $"File not found: {fullPath}");

            // Try to read a few bytes to confirm it's readable and likely an image.
            var bytes = new byte[8];
            await using (var fs = File.OpenRead(fullPath))
            {
                await fs.ReadAsync(bytes, 0, bytes.Length, ct);
            }

            // Basic magic number check for common image formats.
            if (!IsLikelyImageMagicNumber(bytes))
                return (false, $"File doesn't appear to be a valid image (bad magic number): {fullPath}");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Error validating image: {ex.Message}");
        }
    }

    public Task<IEnumerable<string>> ListImagesAsync(string folder, CancellationToken ct = default)
    {
        var folderPath = Path.Combine(ResolveAssetsRoot(), folder);
        if (!Directory.Exists(folderPath))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var files = Directory.GetFiles(folderPath)
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f);

        return Task.FromResult<IEnumerable<string>>(files);
    }

    public Task<bool> DeleteImageAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var fullPath = Path.Combine(ResolveAssetsRoot(), relativePath.TrimStart('/'));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<int> DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        try
        {
            var folderPath = Path.Combine(ResolveAssetsRoot(), folder);
            if (!Directory.Exists(folderPath))
                return Task.FromResult(0);

            var files = Directory.GetFiles(folderPath);
            foreach (var file in files)
            {
                File.Delete(file);
            }

            return Task.FromResult(files.Length);
        }
        catch
        {
            return Task.FromResult(0);
        }
    }

    private static bool IsLikelyImageMagicNumber(byte[] bytes)
    {
        // JPEG: FF D8
        // PNG: 89 50 4E 47
        // GIF: 47 49 46 38
        // WebP: RIFF ... WEBP
        if (bytes.Length < 2)
            return false;

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            return true; // JPEG
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true; // PNG
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return true; // GIF
        if (bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            return true; // RIFF (WebP)

        return false;
    }
}
