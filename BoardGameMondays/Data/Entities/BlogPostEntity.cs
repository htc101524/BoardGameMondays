using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class BlogPostEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(20000)]
    public string Body { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset CreatedOn { get; set; }

    public bool IsAdminOnly { get; set; }

    public string? CreatedByUserId { get; set; }
}
