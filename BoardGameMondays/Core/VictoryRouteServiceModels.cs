namespace BoardGameMondays.Core;

public sealed record VictoryRouteTemplate(Guid Id, Guid GameId, string Name, VictoryRouteType Type, bool IsRequired, int SortOrder, IReadOnlyList<VictoryRouteTemplateOption> Options);

public sealed record VictoryRouteTemplateOption(Guid Id, Guid VictoryRouteId, string Value, int SortOrder);

public sealed record VictoryRouteValue(Guid VictoryRouteId, string? ValueString, bool? ValueBool);
