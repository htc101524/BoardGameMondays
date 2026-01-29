using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoardGameMondays.Data.Entities;

public sealed class UserPurchaseEntity
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// AspNetUsers.Id (the authenticated user).
    /// </summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [ForeignKey(nameof(ShopItem))]
    public Guid ShopItemId { get; set; }

    public ShopItemEntity? ShopItem { get; set; }

    public DateTimeOffset PurchasedOn { get; set; } = DateTimeOffset.UtcNow;
}
