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
            }
        };

        db.ShopItems.AddRange(items);
        await db.SaveChangesAsync();
    }
}
