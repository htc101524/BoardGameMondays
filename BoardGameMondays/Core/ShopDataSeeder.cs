using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

/// <summary>
/// Seeds initial shop items (emoji packs).
/// </summary>
public static class ShopDataSeeder
{
    public static async Task SeedShopItemsAsync(ApplicationDbContext db)
    {
        // Check if any items already exist
        var hasItems = await db.ShopItems.AnyAsync();
        if (hasItems)
        {
            return; // Already seeded
        }

        var items = new[]
        {
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Peach Emoji",
                Description = "That's peachy!",
                Price = 50,
                ItemType = "EmojiPack",
                Data = "🍑",
                MembersOnly = false,
                IsActive = true
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Fire Emoji",
                Description = "That was lit!",
                Price = 50,
                ItemType = "EmojiPack",
                Data = "🔥",
                MembersOnly = false,
                IsActive = true
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Thinking Emoji",
                Description = "Hmm, interesting play...",
                Price = 50,
                ItemType = "EmojiPack",
                Data = "🤔",
                MembersOnly = false,
                IsActive = true
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Dice Emoji",
                Description = "Roll the dice!",
                Price = 50,
                ItemType = "EmojiPack",
                Data = "🎲",
                MembersOnly = false,
                IsActive = true
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Smiling Devil Emoji",
                Description = "Let's get mischievous!",
                Price = 50,
                ItemType = "EmojiPack",
                Data = "😈",
                MembersOnly = false,
                IsActive = true
            },
            // Badge Rings - require wins to purchase
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Bronze Ring",
                Description = "A bronze ring for your badge. Requires 5 wins.",
                Price = 250,
                ItemType = "BadgeRing",
                Data = "bronze",
                MembersOnly = false,
                IsActive = true,
                MinWinsRequired = 5
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Silver Ring",
                Description = "A silver ring for your badge. Requires 25 wins.",
                Price = 500,
                ItemType = "BadgeRing",
                Data = "silver",
                MembersOnly = false,
                IsActive = true,
                MinWinsRequired = 25
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Gold Ring",
                Description = "A gold ring for your badge. Requires 50 wins.",
                Price = 750,
                ItemType = "BadgeRing",
                Data = "gold",
                MembersOnly = false,
                IsActive = true,
                MinWinsRequired = 50
            },
            new ShopItemEntity
            {
                Id = Guid.NewGuid(),
                Name = "Platinum Ring",
                Description = "A platinum ring for your badge. Requires 100 wins.",
                Price = 1000,
                ItemType = "BadgeRing",
                Data = "platinum",
                MembersOnly = false,
                IsActive = true,
                MinWinsRequired = 100
            }
        };

        db.ShopItems.AddRange(items);
        await db.SaveChangesAsync();
    }
}
