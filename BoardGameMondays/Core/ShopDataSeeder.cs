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
        if (!hasItems)
        {
            // Seed all items if none exist
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
                    Description = "Earn your first badge ring with 5 wins",
                    Price = 200,
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
                    Description = "Level up your badge ring with 15 wins",
                    Price = 500,
                    ItemType = "BadgeRing",
                    Data = "silver",
                    MembersOnly = false,
                    IsActive = true,
                    MinWinsRequired = 15
                },
                new ShopItemEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "Gold Ring",
                    Description = "Become a champion with 30 wins",
                    Price = 750,
                    ItemType = "BadgeRing",
                    Data = "gold",
                    MembersOnly = false,
                    IsActive = true,
                    MinWinsRequired = 30
                },
                new ShopItemEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "Platinum Ring",
                    Description = "Achieve elite status with 50 wins",
                    Price = 1000,
                    ItemType = "BadgeRing",
                    Data = "platinum",
                    MembersOnly = false,
                    IsActive = true,
                    MinWinsRequired = 50
                }
            };

            db.ShopItems.AddRange(items);
        }
        else
        {
            // Ensure badge rings exist (they may be missing if shop items were seeded before rings were added)
            var existingRings = await db.ShopItems
                .Where(si => si.ItemType == "BadgeRing")
                .ToDictionaryAsync(r => r.Data);

            var ringDefinitions = new[]
            {
                new { Data = "bronze", Name = "Bronze Ring", Description = "Earn your first badge ring with 5 wins", Price = 200, MinWins = 5 },
                new { Data = "silver", Name = "Silver Ring", Description = "Level up your badge ring with 15 wins", Price = 500, MinWins = 15 },
                new { Data = "gold", Name = "Gold Ring", Description = "Become a champion with 30 wins", Price = 750, MinWins = 30 },
                new { Data = "platinum", Name = "Platinum Ring", Description = "Achieve elite status with 50 wins", Price = 1000, MinWins = 50 }
            };

            foreach (var def in ringDefinitions)
            {
                if (existingRings.TryGetValue(def.Data, out var ring))
                {
                    // Update existing ring
                    ring.Price = def.Price;
                    ring.MinWinsRequired = def.MinWins;
                    ring.Description = def.Description;
                }
                else
                {
                    // Create missing ring
                    db.ShopItems.Add(new ShopItemEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = def.Name,
                        Description = def.Description,
                        Price = def.Price,
                        ItemType = "BadgeRing",
                        Data = def.Data,
                        MembersOnly = false,
                        IsActive = true,
                        MinWinsRequired = def.MinWins
                    });
                }
            }
        }

        await db.SaveChangesAsync();
    }
}
