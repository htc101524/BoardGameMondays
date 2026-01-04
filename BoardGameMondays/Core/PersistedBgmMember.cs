namespace BoardGameMondays.Core;

public sealed class PersistedBgmMember : BgmMember
{
    public PersistedBgmMember(string name, string email, string? summary, string? avatarUrl)
    {
        Name = name;
        Email = email;
        Summary = summary;
        AvatarUrl = avatarUrl;
    }

    public override string Name { get; }

    public override string Email { get; }

    public override string? Summary { get; }

    public override string? AvatarUrl { get; }
}
