using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BoardGameMondays.Core;

public sealed class LocalAssetStorage : IAssetStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly StorageOptions _options;

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
}
