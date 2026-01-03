namespace BoardGameMondays.Core;

public sealed class PersistedBgmMember : BgmMember
{
    public PersistedBgmMember(string name, string email, string? summary)
    {
        Name = name;
        Email = email;
        Summary = summary;
    }

    public override string Name { get; }

    public override string Email { get; }

    public override string? Summary { get; }
}
