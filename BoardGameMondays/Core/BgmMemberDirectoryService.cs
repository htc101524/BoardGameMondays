using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// In-memory directory of all known members (single source of truth for the app).
/// </summary>
public sealed class BgmMemberDirectoryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public BgmMemberDirectoryService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public IReadOnlyList<BgmMember> GetAll()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Members
            .AsNoTracking()
            .Where(m => m.IsBgmMember)
            .OrderBy(m => m.Name)
            .Select(m => (BgmMember)new PersistedBgmMember(m.Name, m.Email, m.Summary, m.AvatarUrl))
            .ToArray();
    }

    public Guid GetOrCreateMemberId(string name)
    {
        using var db = _dbFactory.CreateDbContext();
        var trimmed = InputGuards.RequireTrimmed(name, maxLength: 80, nameof(name), "Name is required.");
        var existing = db.Members.FirstOrDefault(m => m.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = new MemberEntity
        {
            Id = Guid.NewGuid(),
            IsBgmMember = true,
            Name = trimmed,
            Email = $"{trimmed.ToLowerInvariant()}@placeholder.com"
        };

        db.Members.Add(created);
        db.SaveChanges();
        return created.Id;
    }

    public BgmMember GetOrCreate(string name)
    {
        using var db = _dbFactory.CreateDbContext();
        var trimmed = InputGuards.RequireTrimmed(name, maxLength: 80, nameof(name), "Name is required.");
        var existing = db.Members.FirstOrDefault(m => m.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return new PersistedBgmMember(existing.Name, existing.Email, existing.Summary, existing.AvatarUrl);
        }

        var created = new MemberEntity
        {
            Id = Guid.NewGuid(),
            IsBgmMember = true,
            Name = trimmed,
            Email = $"{trimmed.ToLowerInvariant()}@placeholder.com"
        };

        db.Members.Add(created);
        db.SaveChanges();
        return new PersistedBgmMember(created.Name, created.Email, created.Summary, created.AvatarUrl);
    }

    public void AddOrUpdate(BgmMember member)
    {
        if (member is null)
        {
            throw new ArgumentNullException(nameof(member));
        }

        using var db = _dbFactory.CreateDbContext();
        var name = InputGuards.RequireTrimmed(member.Name, maxLength: 80, nameof(member), "Name is required.");

        var existing = db.Members.FirstOrDefault(m => m.Name.ToLower() == name.ToLower());
        if (existing is null)
        {
            db.Members.Add(new MemberEntity
            {
                Id = Guid.NewGuid(),
                IsBgmMember = true,
                Name = name,
                Email = member.Email,
                Summary = member.Summary,
                AvatarUrl = member.AvatarUrl
            });
        }
        else
        {
            existing.Email = member.Email;
            existing.Summary = member.Summary;
            existing.AvatarUrl = member.AvatarUrl;
        }

        db.SaveChanges();
    }
}
