using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class VictoryRouteEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid GameId { get; set; }

    public BoardGameEntity Game { get; set; } = null!;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int Type { get; set; }

    [Required]
    public bool IsRequired { get; set; }

    [Required]
    public int SortOrder { get; set; }

    public List<VictoryRouteOptionEntity> Options { get; set; } = new();
}
