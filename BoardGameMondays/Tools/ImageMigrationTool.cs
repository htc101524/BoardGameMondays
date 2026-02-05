// Image Migration Tool
// Run this via a one-time admin endpoint or as a console command to migrate images from local storage to Azure Blob Storage.
//
// USAGE:
// 1. First, download all images from your Windows App Service using Kudu/FTP
// 2. Place them in a local folder matching the structure: uploads/avatars/*, images/games/*
// 3. Run this tool to upload to Azure Blob Storage and update database URLs

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Tools;

public class ImageMigrationTool
{
    private readonly ApplicationDbContext _db;
    private readonly BlobServiceClient _blobClient;
    private readonly string _containerName;
    private readonly string _blobBaseUrl;

    public ImageMigrationTool(
        ApplicationDbContext db,
        string blobConnectionString,
        string containerName = "bgm-assets",
        string? cdnBaseUrl = null)
    {
        _db = db;
        _blobClient = new BlobServiceClient(blobConnectionString);
        _containerName = containerName;
        _blobBaseUrl = cdnBaseUrl?.TrimEnd('/') 
            ?? $"{_blobClient.Uri.ToString().TrimEnd('/')}/{containerName}";
    }

    /// <summary>
    /// Migrate images from a local folder to Azure Blob Storage and update database URLs.
    /// </summary>
    /// <param name="localAssetsFolder">Path to folder containing uploads/ and images/ subfolders</param>
    public async Task<MigrationResult> MigrateFromLocalFolderAsync(string localAssetsFolder, CancellationToken ct = default)
    {
        var result = new MigrationResult();
        var container = _blobClient.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(publicAccessType: PublicAccessType.Blob, cancellationToken: ct);

        // Migrate avatars
        var avatarsFolder = Path.Combine(localAssetsFolder, "uploads", "avatars");
        if (Directory.Exists(avatarsFolder))
        {
            result.AvatarResults = await MigrateAvatarsAsync(avatarsFolder, container, ct);
        }

        // Migrate game images
        var gamesFolder = Path.Combine(localAssetsFolder, "images", "games");
        if (Directory.Exists(gamesFolder))
        {
            result.GameImageResults = await MigrateGameImagesAsync(gamesFolder, container, ct);
        }

        // Migrate blog images
        var blogFolder = Path.Combine(localAssetsFolder, "uploads", "blog");
        if (Directory.Exists(blogFolder))
        {
            result.BlogImageResults = await MigrateBlogImagesAsync(blogFolder, container, ct);
        }

        return result;
    }

    private async Task<List<ImageMigrationEntry>> MigrateAvatarsAsync(
        string folder, BlobContainerClient container, CancellationToken ct)
    {
        var results = new List<ImageMigrationEntry>();
        var files = Directory.GetFiles(folder);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var blobPath = $"avatars/{fileName}";
            
            try
            {
                // Upload to blob
                var newUrl = await UploadFileToBlobAsync(file, blobPath, container, ct);
                
                // Update database - find member by avatar filename (memberId.ext)
                var memberIdStr = Path.GetFileNameWithoutExtension(fileName);
                if (Guid.TryParse(memberIdStr, out var memberId))
                {
                    var member = await _db.BgmMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct);
                    if (member != null)
                    {
                        var oldUrl = member.AvatarUrl;
                        member.AvatarUrl = newUrl;
                        await _db.SaveChangesAsync(ct);
                        results.Add(new ImageMigrationEntry(fileName, oldUrl, newUrl, true, null));
                    }
                    else
                    {
                        results.Add(new ImageMigrationEntry(fileName, null, newUrl, true, "Uploaded but no matching member in DB"));
                    }
                }
                else
                {
                    results.Add(new ImageMigrationEntry(fileName, null, newUrl, true, "Uploaded but filename not a valid GUID"));
                }
            }
            catch (Exception ex)
            {
                results.Add(new ImageMigrationEntry(fileName, null, null, false, ex.Message));
            }
        }

        return results;
    }

    private async Task<List<ImageMigrationEntry>> MigrateGameImagesAsync(
        string folder, BlobContainerClient container, CancellationToken ct)
    {
        var results = new List<ImageMigrationEntry>();
        var files = Directory.GetFiles(folder);

        // Build a map of old URLs to games
        var games = await _db.Games.ToListAsync(ct);
        var urlToGame = games
            .Where(g => !string.IsNullOrWhiteSpace(g.ImageUrl))
            .ToDictionary(g => g.ImageUrl!, g => g);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var blobPath = $"games/{fileName}";
            var oldUrl = $"/images/games/{fileName}";
            
            try
            {
                var newUrl = await UploadFileToBlobAsync(file, blobPath, container, ct);

                // Check for URL with or without query string
                var matchingGame = urlToGame.Keys
                    .Where(k => k.StartsWith(oldUrl))
                    .Select(k => urlToGame[k])
                    .FirstOrDefault();

                if (matchingGame != null)
                {
                    matchingGame.ImageUrl = newUrl;
                    await _db.SaveChangesAsync(ct);
                    results.Add(new ImageMigrationEntry(fileName, oldUrl, newUrl, true, null));
                }
                else
                {
                    results.Add(new ImageMigrationEntry(fileName, oldUrl, newUrl, true, "Uploaded but no matching game in DB"));
                }
            }
            catch (Exception ex)
            {
                results.Add(new ImageMigrationEntry(fileName, oldUrl, null, false, ex.Message));
            }
        }

        return results;
    }

    private async Task<List<ImageMigrationEntry>> MigrateBlogImagesAsync(
        string folder, BlobContainerClient container, CancellationToken ct)
    {
        var results = new List<ImageMigrationEntry>();
        var files = Directory.GetFiles(folder);

        // Blog images are embedded in markdown, so we need to update BlogPost.Body
        var posts = await _db.BlogPosts.ToListAsync(ct);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var blobPath = $"blog/{fileName}";
            var oldUrl = $"/uploads/blog/{fileName}";
            
            try
            {
                var newUrl = await UploadFileToBlobAsync(file, blobPath, container, ct);

                // Update any blog posts that reference this image
                var updated = false;
                foreach (var post in posts)
                {
                    if (post.Body?.Contains(oldUrl) == true)
                    {
                        post.Body = post.Body.Replace(oldUrl, newUrl);
                        updated = true;
                    }
                }

                if (updated)
                {
                    await _db.SaveChangesAsync(ct);
                }

                results.Add(new ImageMigrationEntry(fileName, oldUrl, newUrl, true, updated ? null : "Uploaded but not referenced in any blog post"));
            }
            catch (Exception ex)
            {
                results.Add(new ImageMigrationEntry(fileName, oldUrl, null, false, ex.Message));
            }
        }

        return results;
    }

    private async Task<string> UploadFileToBlobAsync(
        string localPath, string blobPath, BlobContainerClient container, CancellationToken ct)
    {
        var blob = container.GetBlobClient(blobPath);
        var extension = Path.GetExtension(localPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };

        await using var stream = File.OpenRead(localPath);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);

        return $"{_blobBaseUrl}/{blobPath}";
    }

    public record MigrationResult
    {
        public List<ImageMigrationEntry> AvatarResults { get; set; } = new();
        public List<ImageMigrationEntry> GameImageResults { get; set; } = new();
        public List<ImageMigrationEntry> BlogImageResults { get; set; } = new();

        public int TotalFiles => AvatarResults.Count + GameImageResults.Count + BlogImageResults.Count;
        public int SuccessCount => AvatarResults.Count(r => r.Success) + GameImageResults.Count(r => r.Success) + BlogImageResults.Count(r => r.Success);
    }

    public record ImageMigrationEntry(string FileName, string? OldUrl, string? NewUrl, bool Success, string? Note);
}
