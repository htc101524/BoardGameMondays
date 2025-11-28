namespace BoardGameMondays.Core;
public class DemoBgmMember : BgmMember
{
    private readonly string _name;
    public DemoBgmMember(string name) => _name = name;
    public override string Name => _name;
    public override string Email => $"{_name.ToLower()}@placeholder.com";
}
