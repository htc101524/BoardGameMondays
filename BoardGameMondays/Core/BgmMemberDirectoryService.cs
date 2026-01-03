using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// In-memory directory of all known members (single source of truth for the app).
/// </summary>
public sealed class BgmMemberDirectoryService
{
    private readonly ApplicationDbContext _db;

    public BgmMemberDirectoryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<BgmMember> GetAll()
        => _db.Members
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => (BgmMember)new PersistedBgmMember(m.Name, m.Email, m.Summary))
            .ToArray();

    public BgmMember GetOrCreate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var trimmed = name.Trim();
        var existing = _db.Members.FirstOrDefault(m => m.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return new PersistedBgmMember(existing.Name, existing.Email, existing.Summary);
        }

        var created = new MemberEntity
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            Email = $"{trimmed.ToLowerInvariant()}@placeholder.com"
        };

        _db.Members.Add(created);
        _db.SaveChanges();
        return new PersistedBgmMember(created.Name, created.Email, created.Summary);
    }

    public void AddOrUpdate(BgmMember member)
    {
        if (member is null)
        {
            throw new ArgumentNullException(nameof(member));
        }

        var existing = _db.Members.FirstOrDefault(m => m.Name.ToLower() == member.Name.ToLower());
        if (existing is null)
        {
            _db.Members.Add(new MemberEntity
            {
                Id = Guid.NewGuid(),
                Name = member.Name,
                Email = member.Email,
                Summary = member.Summary
            });
        }
        else
        {
            existing.Email = member.Email;
            existing.Summary = member.Summary;
        }

        _db.SaveChanges();
    }
}
