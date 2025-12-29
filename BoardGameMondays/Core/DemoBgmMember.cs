namespace BoardGameMondays.Core;
public class DemoBgmMember : BgmMember
{
    private readonly string _name;
    private readonly string? _summary;

    public DemoBgmMember(string name, string? summary = null)
    {
        _name = name;
        _summary = summary;
    }
    public override string Name => _name;
    public override string Email => $"{_name.ToLower()}@placeholder.com";

    public override string? Summary => _summary;
}
