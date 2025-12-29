using System.Collections.Concurrent;

namespace BoardGameMondays.Core;

/// <summary>
/// In-memory directory of all known members (single source of truth for the app).
/// </summary>
public sealed class BgmMemberDirectoryService
{
    private readonly ConcurrentDictionary<string, BgmMember> _members = new(StringComparer.OrdinalIgnoreCase);

    public BgmMemberDirectoryService()
    {
        // Seed a few demo members so the People page isn't empty.
        AddOrUpdate(new DemoBgmMember("Henry", summary: "Organizes the Monday game nights."));
        AddOrUpdate(new DemoBgmMember("Alex", summary: "Loves puzzly euros and clever drafting."));
        AddOrUpdate(new DemoBgmMember("Sam", summary: "Always up for a fast teach and a rematch."));
    }

    public IReadOnlyList<BgmMember> GetAll()
        => _members.Values
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public BgmMember GetOrCreate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return _members.GetOrAdd(name.Trim(), n => new DemoBgmMember(n));
    }

    public void AddOrUpdate(BgmMember member)
    {
        if (member is null)
        {
            throw new ArgumentNullException(nameof(member));
        }

        _members[member.Name] = member;
    }
}
