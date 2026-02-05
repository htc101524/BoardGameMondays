using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BoardGameMondays.Core;

public sealed class BlogService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public BlogService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<BlogPost>> GetLatestAsync(int take = 10, bool includeAdminOnly = false, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return Array.Empty<BlogPost>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.BlogPosts.AsNoTracking();
        if (!includeAdminOnly)
        {
            query = query.Where(p => !p.IsAdminOnly);
        }

        return await query
            .OrderByDescending(p => p.CreatedOn)
            .Take(take)
            .Select(p => new BlogPost(p.Id, p.Title, p.Slug, p.Body, p.CreatedOn, p.IsAdminOnly))
            .ToListAsync(ct);
    }

    public async Task<BlogPost> AddAsync(string title, string body, bool isAdminOnly, string? createdByUserId, CancellationToken ct = default)
    {
        title = InputGuards.RequireTrimmed(title, maxLength: 120, nameof(title), "Title is required.");
        body = InputGuards.RequireTrimmed(body, maxLength: 20_000, nameof(body), "Body is required.");

        var slugBase = ToSlug(title);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var slug = await EnsureUniqueSlugAsync(db, slugBase, ct);

        var entity = new BlogPostEntity
        {
            Id = Guid.NewGuid(),
            Title = title,
            Slug = slug,
            Body = body,
            CreatedOn = DateTimeOffset.UtcNow,
            IsAdminOnly = isAdminOnly,
            CreatedByUserId = createdByUserId
        };

        db.BlogPosts.Add(entity);
        await db.SaveChangesAsync(ct);

        Changed?.Invoke();

        return new BlogPost(entity.Id, entity.Title, entity.Slug, entity.Body, entity.CreatedOn, entity.IsAdminOnly);
    }

    public async Task<BlogPost?> GetByIdAsync(Guid id, bool includeAdminOnly = false, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.BlogPosts.AsNoTracking();
        if (!includeAdminOnly)
        {
            query = query.Where(p => !p.IsAdminOnly);
        }

        var entity = await query
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return entity is null ? null : new BlogPost(entity.Id, entity.Title, entity.Slug, entity.Body, entity.CreatedOn, entity.IsAdminOnly);
    }

    public async Task<BlogPost?> GetBySlugAsync(string slug, bool includeAdminOnly = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.BlogPosts.AsNoTracking();
        if (!includeAdminOnly)
        {
            query = query.Where(p => !p.IsAdminOnly);
        }

        var entity = await query
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

        return entity is null ? null : new BlogPost(entity.Id, entity.Title, entity.Slug, entity.Body, entity.CreatedOn, entity.IsAdminOnly);
    }

    public async Task<BlogPost?> UpdateAsync(Guid id, string title, string body, bool isAdminOnly, CancellationToken ct = default)
    {
        title = InputGuards.RequireTrimmed(title, maxLength: 120, nameof(title), "Title is required.");
        body = InputGuards.RequireTrimmed(body, maxLength: 20_000, nameof(body), "Body is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null)
        {
            return null;
        }

        entity.Title = title;
        entity.Body = body;
        entity.IsAdminOnly = isAdminOnly;

        await db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return new BlogPost(entity.Id, entity.Title, entity.Slug, entity.Body, entity.CreatedOn, entity.IsAdminOnly);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entity = await db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null)
        {
            return false;
        }

        db.BlogPosts.Remove(entity);
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();

        return true;
    }

    private static async Task<string> EnsureUniqueSlugAsync(ApplicationDbContext db, string slugBase, CancellationToken ct)
    {
        var slug = slugBase;
        var i = 2;

        while (await db.BlogPosts.AsNoTracking().AnyAsync(p => p.Slug == slug, ct))
        {
            slug = $"{slugBase}-{i}";
            i++;
        }

        return slug;
    }

    private static string ToSlug(string input)
    {
        input = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (input.Length == 0)
        {
            return "post";
        }

        var sb = new StringBuilder(input.Length);
        var lastDash = false;

        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.')
            {
                if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
        }

        var slug = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "post" : slug;
    }

    public sealed record BlogPost(Guid Id, string Title, string Slug, string Body, DateTimeOffset CreatedOn, bool IsAdminOnly);
}
