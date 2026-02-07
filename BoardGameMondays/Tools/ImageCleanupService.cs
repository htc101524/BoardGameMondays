using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BoardGameMondays.Core;
using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Tools;

/// <summary>
/// Cleans up orphaned image files from storage.
/// Identifies files that exist in storage but are not referenced in the database.
/// Optionally deletes them to save storage space.
/// NEW: Provides automated garbage collection for migrated images.
/// </summary>
public sealed class ImageCleanupService
{
    private readonly ApplicationDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly ImageAuditLogger _auditLogger;

    public ImageCleanupService(
        ApplicationDbContext db,
        IAssetStorage storage,
        ImageAuditLogger auditLogger)
    {
        _db = db;
        _storage = storage;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Find all orphaned images (files in storage not referenced in database).
    /// Does not delete anything; just reports findings.
    /// </summary>
    public async Task<CleanupReport> FindOrphanedImagesAsync(CancellationToken ct = default)
    {
        var report = new CleanupReport();

        // Get all referenced URLs from database.
        var dbAvatarUrls = await _db.Members
            .Where(m => m.AvatarUrl != null)
            .Select(m => m.AvatarUrl!)
            .ToListAsync(ct);

        var dbGameUrls = await _db.Games
            .Where(g => g.ImageUrl != null)
            .Select(g => g.ImageUrl!)
            .ToListAsync(ct);

        var blogPostUrls = new List<string>();
        var blogPosts = await _db.BlogPosts.ToListAsync(ct);
        foreach (var post in blogPosts)
        {
            var urls = BlogImageMigrationHelper.ExtractImageUrls(post.Body);
            blogPostUrls.AddRange(urls);
        }

        var allDbUrls = new HashSet<string>(dbAvatarUrls.Concat(dbGameUrls).Concat(blogPostUrls));

        // Check each storage folder for orphans.
        await FindOrphanedInFolderAsync("avatars", allDbUrls, report, ct);
        await FindOrphanedInFolderAsync("games", allDbUrls, report, ct);
        await FindOrphanedInFolderAsync("blog", allDbUrls, report, ct);

        report.CompletedAt = DateTimeOffset.UtcNow;

        return report;
    }

    /// <summary>
    /// Delete all orphaned images identified by FindOrphanedImagesAsync.
    /// WARNING: This is destructive. Review the report first.
    /// </summary>
    public async Task<CleanupResult> DeleteOrphanedImagesAsync(CleanupReport report, CancellationToken ct = default)
    {
        var result = new CleanupResult { ReportIds = report.OrphanedFiles.Select(f => f.Id).ToList() };

        int deletedCount = 0;
        var failedDeletions = new List<string>();

        foreach (var orphan in report.OrphanedFiles)
        {
            try
            {
                var success = await _storage.DeleteImageAsync(orphan.RelativePath, ct);
                if (success)
                {
                    deletedCount++;

                    await _auditLogger.LogImageOperationAsync(
                        ImageOperationType.Cleanup,
                        orphan.RelativePath,
                        orphan.Folder,
                        true,
                        null,
                        new Dictionary<string, string>
                        {
                            { "Reason", "OrphanedFile" },
                            { "FileSizeBytes", orphan.FileSizeBytes.ToString() }
                        },
                        ct);
                }
                else
                {
                    failedDeletions.Add(orphan.RelativePath);
                }
            }
            catch (Exception ex)
            {
                failedDeletions.Add(orphan.RelativePath);

                await _auditLogger.LogImageOperationAsync(
                    ImageOperationType.Cleanup,
                    orphan.RelativePath,
                    orphan.Folder,
                    false,
                    ex.Message,
                    null,
                    ct);
            }
        }

        result.DeletedCount = deletedCount;
        result.FailedDeletions = failedDeletions;
        result.CompletedAt = DateTimeOffset.UtcNow;

        return result;
    }

    /// <summary>
    /// Delete entire folder (use with caution).
    /// Useful for cleanup after migration (e.g., delete old Local images after moving to Azure).
    /// </summary>
    public async Task<int> DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        try
        {
            var deletedCount = await _storage.DeleteFolderAsync(folder, ct);

            await _auditLogger.LogImageOperationAsync(
                ImageOperationType.Cleanup,
                $"folder:{folder}",
                null,
                true,
                null,
                new Dictionary<string, string> { { "FilesDeleted", deletedCount.ToString() } },
                ct);

            return deletedCount;
        }
        catch (Exception ex)
        {
            await _auditLogger.LogImageOperationAsync(
                ImageOperationType.Cleanup,
                $"folder:{folder}",
                null,
                false,
                ex.Message,
                null,
                ct);

            return 0;
        }
    }

    /// <summary>
    /// Verify storage integrity: compare database references with actual storage.
    /// Reports missing files, orphaned files, and inconsistencies.
    /// </summary>
    public async Task<StorageIntegrityReport> VerifyStorageIntegrityAsync(CancellationToken ct = default)
    {
        var report = new StorageIntegrityReport();

        // Get database references.
        var dbAvatarUrls = await _db.Members
            .Where(m => m.AvatarUrl != null)
            .Select(m => m.AvatarUrl!)
            .ToListAsync(ct);

        var dbGameUrls = await _db.Games
            .Where(g => g.ImageUrl != null)
            .Select(g => g.ImageUrl!)
            .ToListAsync(ct);

        var blogPostUrls = new List<string>();
        var blogPosts = await _db.BlogPosts.ToListAsync(ct);
        foreach (var post in blogPosts)
        {
            var urls = BlogImageMigrationHelper.ExtractImageUrls(post.Body);
            blogPostUrls.AddRange(urls);
        }

        // Check for missing files (referenced in DB but not in storage).
        foreach (var url in dbAvatarUrls)
        {
            var validation = await _storage.ValidateImageAsync(url, ct);
            if (!validation.isValid)
            {
                report.MissingFiles.Add(new MissingFileEntry
                {
                    Url = url,
                    Type = "Avatar",
                    ErrorMessage = validation.error
                });
            }
        }

        foreach (var url in dbGameUrls)
        {
            var validation = await _storage.ValidateImageAsync(url, ct);
            if (!validation.isValid)
            {
                report.MissingFiles.Add(new MissingFileEntry
                {
                    Url = url,
                    Type = "GameImage",
                    ErrorMessage = validation.error
                });
            }
        }

        foreach (var url in blogPostUrls)
        {
            var validation = await _storage.ValidateImageAsync(url, ct);
            if (!validation.isValid)
            {
                report.MissingFiles.Add(new MissingFileEntry
                {
                    Url = url,
                    Type = "BlogImage",
                    ErrorMessage = validation.error
                });
            }
        }

        // Find orphaned files.
        var orphanReport = await FindOrphanedImagesAsync(ct);
        report.OrphanedFiles = orphanReport.OrphanedFiles;

        report.IsHealthy = report.MissingFiles.Count == 0 && report.OrphanedFiles.Count == 0;
        report.CompletedAt = DateTimeOffset.UtcNow;

        return report;
    }

    private async Task FindOrphanedInFolderAsync(
        string folder,
        HashSet<string> dbUrls,
        CleanupReport report,
        CancellationToken ct)
    {
        try
        {
            var storageFiles = await _storage.ListImagesAsync(folder, ct);
            if (storageFiles == null)
                return;

            foreach (var fileName in storageFiles)
            {
                var relativePath = $"{folder}/{fileName}";

                // Check if this file is referenced in the database.
                var isReferenced = dbUrls.Any(url => url.Contains(fileName));

                if (!isReferenced)
                {
                    report.OrphanedFiles.Add(new OrphanedFileEntry
                    {
                        Id = Guid.NewGuid(),
                        RelativePath = relativePath,
                        Folder = folder,
                        FileName = fileName,
                        FileSizeBytes = 0 // TODO: Get size from storage metadata.
                    });
                }
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add($"Error scanning folder '{folder}': {ex.Message}");
        }
    }
}

/// <summary>
/// Report of cleanup operation findings.
/// </summary>
public class CleanupReport
{
    public List<OrphanedFileEntry> OrphanedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTimeOffset CompletedAt { get; set; }

    public int TotalOrphans => OrphanedFiles.Count;
    public long TotalOrphanSizeBytes => OrphanedFiles.Sum(f => f.FileSizeBytes);
}

/// <summary>
/// Entry for an orphaned file.
/// </summary>
public class OrphanedFileEntry
{
    public Guid Id { get; set; }
    public string RelativePath { get; set; } = "";
    public string Folder { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
}

/// <summary>
/// Result of cleanup operation.
/// </summary>
public class CleanupResult
{
    public List<Guid> ReportIds { get; set; } = new();
    public int DeletedCount { get; set; }
    public List<string> FailedDeletions { get; set; } = new();
    public DateTimeOffset CompletedAt { get; set; }

    public bool HasFailures => FailedDeletions.Count > 0;
}

/// <summary>
/// Report of storage integrity verification.
/// </summary>
public class StorageIntegrityReport
{
    public List<MissingFileEntry> MissingFiles { get; set; } = new();
    public List<OrphanedFileEntry> OrphanedFiles { get; set; } = new();
    public bool IsHealthy { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

/// <summary>
/// Entry for a missing file (referenced in DB but not in storage).
/// </summary>
public class MissingFileEntry
{
    public string Url { get; set; } = "";
    public string Type { get; set; } = "";
    public string? ErrorMessage { get; set; }
}
