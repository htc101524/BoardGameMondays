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
            // Update existing badge rings with correct prices and win requirements
            var badgeRings = await db.ShopItems
                .Where(si => si.ItemType == "BadgeRing")
                .ToListAsync();

            var ringUpdates = new Dictionary<string, (int Price, int MinWins)>
            {
                { "bronze", (200, 5) },
                { "silver", (500, 15) },
                { "gold", (750, 30) },
                { "platinum", (1000, 50) }
            };

            foreach (var ring in badgeRings)
            {
                if (ringUpdates.TryGetValue(ring.Data, out var update))
                {
                    ring.Price = update.Price;
                    ring.MinWinsRequired = update.MinWins;
                    ring.Description = ring.Data switch
                    {
                        "bronze" => "Earn your first badge ring with 5 wins",
                        "silver" => "Level up your badge ring with 15 wins",
                        "gold" => "Become a champion with 30 wins",
                        "platinum" => "Achieve elite status with 50 wins",
                        _ => ring.Description
                    };
                }
            }

            db.ShopItems.UpdateRange(badgeRings);
        }

        await db.SaveChangesAsync();
    }
}
