using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class FeaturedStateEntity
{
    [Key]
    public int Id { get; set; }

    public Guid? FeaturedGameId { get; set; }
}
