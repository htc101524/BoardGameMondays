using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace BoardGameMondays.Core;

/// <summary>
/// In-memory directory of all known members (single source of truth for the app).
/// </summary>
public sealed class BgmMemberDirectoryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    // Cache keys
    private const string AllMembersCacheKey = "Members:All";
    private const string AdminsCacheKey = "Members:Admins";

    // Members rarely change - cache for longer
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(15);

    public BgmMemberDirectoryService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IConfiguration configuration,
        IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
        _cache = cache;
    }

    public IReadOnlyList<BgmMember> GetAll()
    {
        return _cache.GetOrCreate(AllMembersCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = DefaultCacheDuration;
            
            using var db = _dbFactory.CreateDbContext();
            return db.Members
                .AsNoTracking()
                .Where(m => m.IsBgmMember)
                .OrderBy(m => m.Name)
                .Select(m => (BgmMember)new PersistedBgmMember(m.Name, m.Email, m.Summary, m.AvatarUrl))
                .ToArray();
        }) ?? [];
    }

    public IReadOnlyList<BgmMember> GetAdmins()
    {
        return _cache.GetOrCreate(AdminsCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = DefaultCacheDuration;
            return GetAdminsInternal();
        }) ?? [];
    }

    private IReadOnlyList<BgmMember> GetAdminsInternal()
    {
        using var db = _dbFactory.CreateDbContext();

        // Admins are defined via configuration (see AdminRoleClaimsTransformation). In development,
        // the role may also be persisted via Identity; we fall back to DB roles if no config is set.
        var configuredUserNames = _configuration.GetSection("Security:Admins:UserNames").Get<string[]>() ?? [];
        // IMPORTANT: Keep this as a List<string> to avoid overload resolution choosing
        // MemoryExtensions.Contains(ReadOnlySpan<string>, ...) which introduces ref-struct types
        // into EF expression trees (can crash at runtime).
        var normalizedConfigured = configuredUserNames
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> adminUserIds;
        if (normalizedConfigured.Count > 0)
        {
            adminUserIds = db.Users
                .AsNoTracking()
            .Where(u => u.NormalizedUserName != null && normalizedConfigured.Contains(u.NormalizedUserName))
                .Select(u => u.Id)
                .ToList();
        }
        else
        {
            var adminRoleId = db.Roles
                .AsNoTracking()
                .Where(r => r.Name == "Admin")
                .Select(r => r.Id)
                .FirstOrDefault();

            adminUserIds = string.IsNullOrWhiteSpace(adminRoleId)
                ? []
                : db.UserRoles
                    .AsNoTracking()
                    .Where(ur => ur.RoleId == adminRoleId)
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .ToList();
        }

        if (adminUserIds.Count == 0)
        {
            return Array.Empty<BgmMember>();
        }

        const string memberIdClaimType = "bgm:memberId";
        const string displayNameClaimType = "bgm:displayName";

        var adminClaims = db.UserClaims
            .AsNoTracking()
            .Where(c => adminUserIds.Contains(c.UserId)
                && (c.ClaimType == memberIdClaimType || c.ClaimType == displayNameClaimType))
            .Select(c => new { c.ClaimType, c.ClaimValue })
            .ToList();

        var memberIds = adminClaims
            .Where(c => c.ClaimType == memberIdClaimType)
            .Select(c => c.ClaimValue)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var displayNames = adminClaims
            .Where(c => c.ClaimType == displayNameClaimType)
            .Select(c => c.ClaimValue?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.ToLowerInvariant())
            .Distinct()
            .ToArray();

        var members = new List<MemberEntity>();

        if (memberIds.Length > 0)
        {
            var memberIdSet = memberIds.ToHashSet();
            members.AddRange(db.Members
                .AsNoTracking()
                .Where(m => m.IsBgmMember)
                .AsEnumerable()
                .Where(m => memberIdSet.Contains(m.Id))
                .ToList());
        }

        if (displayNames.Length > 0)
        {
            var displayNameSet = displayNames.ToHashSet();
            members.AddRange(db.Members
                .AsNoTracking()
                .Where(m => m.IsBgmMember)
                .AsEnumerable()
                .Where(m => displayNameSet.Contains(m.Name.ToLowerInvariant()))
                .ToList());
        }

        return members
            .DistinctBy(m => m.Id)
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
            Email = string.Empty
        };

        db.Members.Add(created);
        db.SaveChanges();
        InvalidateCache();
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
            Email = string.Empty
        };

        db.Members.Add(created);
        db.SaveChanges();
        InvalidateCache();
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
        InvalidateCache();
    }

    /// <summary>
    /// Invalidates all member caches. Called automatically after write operations.
    /// </summary>
    private void InvalidateCache()
    {
        _cache.Remove(AllMembersCacheKey);
        _cache.Remove(AdminsCacheKey);
    }
}
