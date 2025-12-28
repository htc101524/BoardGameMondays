namespace BoardGameMondays.Core;

public sealed class EmptyOverview : Overview
{
    public static EmptyOverview Instance { get; } = new();

    private EmptyOverview() { }
}
