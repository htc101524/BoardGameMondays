namespace BoardGameMondays.Core;

public abstract class BgmMember
{
    public abstract string Name { get; }
    public abstract string Email { get; }

    public virtual string? Summary => null;
}