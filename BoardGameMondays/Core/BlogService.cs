using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BoardGameMondays.Core;

public sealed class BlogService
{
    private readonly ApplicationDbContext _db;

    public BlogService(ApplicationDbContext db)
    {
        _db = db;
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<BlogPost>> GetLatestAsync(int take = 10, CancellationToken ct = default)
    {
        if (take <= 0)
        {
            return Array.Empty<BlogPost>();
        }

        return await _db.BlogPosts
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedOn)
            .Take(take)
            .Select(p => new BlogPost(p.Id, p.Title, p.Slug, p.Body, p.CreatedOn))
            .ToListAsync(ct);
    }

    public async Task<BlogPost> AddAsync(string title, string body, string? createdByUserId, CancellationToken ct = default)
    {
        title = (title ?? string.Empty).Trim();
        body = (body ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Body is required.", nameof(body));
        }

        var slugBase = ToSlug(title);
        var slug = await EnsureUniqueSlugAsync(slugBase, ct);

        var entity = new BlogPostEntity
        {
            Id = Guid.NewGuid(),
            Title = title,
            Slug = slug,
            Body = body,
            CreatedOn = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };

        _db.BlogPosts.Add(entity);
        await _db.SaveChangesAsync(ct);

        Changed?.Invoke();

        return new BlogPost(entity.Id, entity.Title, entity.Slug, entity.Body, entity.CreatedOn);
    }

    private async Task<string> EnsureUniqueSlugAsync(string slugBase, CancellationToken ct)
    {
        var slug = slugBase;
        var i = 2;

        while (await _db.BlogPosts.AsNoTracking().AnyAsync(p => p.Slug == slug, ct))
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

    public sealed record BlogPost(Guid Id, string Title, string Slug, string Body, DateTimeOffset CreatedOn);
}
