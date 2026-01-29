using System.ComponentModel.DataAnnotations;

namespace BoardGameMondays.Data.Entities;

public sealed class ShopItemEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Price in coins.
    /// </summary>
    [Required]
    public int Price { get; set; }

    /// <summary>
    /// Type of shop item: "EmojiPack" or "BadgeCosmetic"
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ItemType { get; set; } = "EmojiPack";

    /// <summary>
    /// Comma-separated list of emojis for emoji packs, or JSON data for other types.
    /// For EmojiPack: "👍,❌,🏆,🎉,🎲,🔥"
    /// For BadgeCosmetic: {"icon":"⭐","color":"#FFD700"}
    /// </summary>
    [Required]
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Whether this item is currently available for purchase.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether only members (admins) can buy this.
    /// </summary>
    public bool MembersOnly { get; set; } = false;

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
}
