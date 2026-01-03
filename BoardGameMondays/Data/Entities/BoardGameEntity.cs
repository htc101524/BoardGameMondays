using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class BoardGameEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int Status { get; set; }

    [MaxLength(512)]
    public string? Tagline { get; set; }

    [MaxLength(512)]
    public string? ImageUrl { get; set; }

    public List<ReviewEntity> Reviews { get; set; } = new();
}
