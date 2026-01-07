using Microsoft.AspNetCore.Hosting;

namespace BoardGameMondays.Core;

public sealed class LocalAssetStorage : IAssetStorage
{
    private readonly IWebHostEnvironment _env;

    public LocalAssetStorage(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<string> SaveAvatarAsync(Guid memberId, Stream content, string extension, CancellationToken ct = default)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "avatars");
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
        var absolutePath = Path.Combine(_env.WebRootPath, "images", "games", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var outStream = File.Create(absolutePath))
        {
            await content.CopyToAsync(outStream, ct);
        }

        return $"/images/games/{fileName}";
    }
}
