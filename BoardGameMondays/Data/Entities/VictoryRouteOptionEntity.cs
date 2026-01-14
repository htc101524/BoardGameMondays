using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class VictoryRouteOptionEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid VictoryRouteId { get; set; }

    public VictoryRouteEntity VictoryRoute { get; set; } = null!;

    [Required]
    [MaxLength(128)]
    public string Value { get; set; } = string.Empty;

    [Required]
    public int SortOrder { get; set; }
}
