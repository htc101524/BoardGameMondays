using BoardGameMondays.Data;
using BoardGameMondays.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core;

public enum PurchaseResult
{
    Success,
    NotFound,
    AlreadyOwned,
    InsufficientCoins,
    InsufficientWins,
    Failed
}

public sealed class ShopService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly BgmCoinService _coinService;

    public ShopService(IDbContextFactory<ApplicationDbContext> dbFactory, BgmCoinService coinService)
    {
        _dbFactory = dbFactory;
        _coinService = coinService;
    }

    /// <summary>
    /// Gets active shop items available for purchase.
    /// </summary>
    public async Task<IReadOnlyList<ShopItem>> GetAvailableItemsAsync(bool membersOnly = false, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var items = await db.ShopItems
            .AsNoTracking()
            .Where(i => i.IsActive && (membersOnly || !i.MembersOnly))
            .OrderBy(i => i.Price)
            .ToListAsync(ct);

        return items.Select(e => ToDomain(e)).ToArray();
    }

    /// <summary>
    /// Gets ALL shop items (including inactive) for admin management.
    /// </summary>
    public async Task<IReadOnlyList<ShopItem>> GetAllItemsForAdminAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var items = await db.ShopItems
            .AsNoTracking()
            .OrderBy(i => i.Name)
            .ToListAsync(ct);

        return items.Select(e => ToAdminDomain(e)).ToArray();
    }

    /// <summary>
    /// Creates a new shop item.
    /// </summary>
    public async Task<Guid> CreateItemAsync(string name, string description, int price, string itemType, string data, bool membersOnly, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var item = new ShopItemEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Price = price,
            ItemType = itemType,
            Data = data,
            MembersOnly = membersOnly,
            IsActive = isActive
        };

        db.ShopItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>
    /// Updates an existing shop item.
    /// </summary>
    public async Task<bool> UpdateItemAsync(Guid itemId, string name, string description, int price, string data, bool membersOnly, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var item = await db.ShopItems.FindAsync(new object[] { itemId }, ct);
        if (item is null)
        {
            return false;
        }

        if (item.ItemType == "BadgeRing")
        {
            return false;
        }

        item.Name = name;
        item.Description = description;
        item.Price = price;
        item.Data = data;
        item.MembersOnly = membersOnly;
        item.IsActive = isActive;

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Deletes a shop item (if no purchases exist).
    /// </summary>
    public async Task<bool> DeleteItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var item = await db.ShopItems.FindAsync(new object[] { itemId }, ct);
        if (item is null)
        {
            return false;
        }

        if (item.ItemType == "BadgeRing")
        {
            return false;
        }

        // Check if anyone has purchased this item
        var hasPurchases = await db.UserPurchases
            .AsNoTracking()
            .AnyAsync(p => p.ShopItemId == itemId, ct);

        if (hasPurchases)
        {
            return false; // Cannot delete if purchases exist
        }

        db.ShopItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Gets items the user has purchased.
    /// </summary>
    public async Task<IReadOnlyList<ShopItem>> GetUserPurchasedItemsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var items = await db.UserPurchases
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Join(db.ShopItems.AsNoTracking(), p => p.ShopItemId, i => i.Id, (p, i) => i)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);

        return items.Select(ToDomain).ToArray();
    }

    /// <summary>
    /// Gets all emoji options available to the user (only purchased packs, no defaults).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetUserEmojisAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var purchases = await db.UserPurchases
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Join(db.ShopItems.AsNoTracking().Where(i => i.ItemType == "EmojiPack"), 
                  p => p.ShopItemId, i => i.Id, (p, i) => i.Data)
            .ToListAsync(ct);

        var allEmojis = new HashSet<string>();
        
        foreach (var pack in purchases)
        {
            var emojis = pack.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e));
            foreach (var emoji in emojis)
            {
                allEmojis.Add(emoji);
            }
        }

        return allEmojis.OrderBy(e => e).ToList();
    }

    /// <summary>
    /// Purchases an item, deducting coins from the user.
    /// Returns null if purchase fails (insufficient coins, item not found, already owned, insufficient wins).
    /// </summary>
    public async Task<PurchaseResult> PurchaseItemAsync(string userId, Guid shopItemId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var item = await db.ShopItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == shopItemId && i.IsActive, ct);

        if (item is null)
        {
            return PurchaseResult.NotFound;
        }

        // Check if user already owns this item
        var alreadyOwns = await db.UserPurchases
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId && p.ShopItemId == shopItemId, ct);

        if (alreadyOwns)
        {
            return PurchaseResult.AlreadyOwned;
        }

        // Check coin balance
        var coins = await _coinService.GetCoinsAsync(userId, ct);
        if (coins < item.Price)
        {
            return PurchaseResult.InsufficientCoins;
        }

        // Check win requirement if item requires wins
        if (item.MinWinsRequired > 0)
        {
            // Get the user's member ID from their claims
            var memberIdClaim = await db.UserClaims
                .AsNoTracking()
                .Where(c => c.UserId == userId && c.ClaimType == "bgm:memberId")
                .Select(c => c.ClaimValue)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(memberIdClaim) || !Guid.TryParse(memberIdClaim, out var memberId))
            {
                return PurchaseResult.InsufficientWins;
            }

            // Count the user's wins
            var winCount = await db.GameNightGames
                .AsNoTracking()
                .Where(g => g.WinnerMemberId == memberId && g.IsPlayed)
                .CountAsync(ct);

            if (winCount < item.MinWinsRequired)
            {
                return PurchaseResult.InsufficientWins;
            }
        }

        // Deduct coins and record purchase in transaction
        var strategy = db.Database.CreateExecutionStrategy();
        var success = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            try
            {
                // Deduct coins only if the item has a cost
                if (item.Price > 0)
                {
                    var spendSuccess = await _coinService.TrySpendAsync(db, userId, item.Price, ct);
                    if (!spendSuccess)
                    {
                        await tx.RollbackAsync(ct);
                        return false;
                    }
                }

                // Record purchase
                db.UserPurchases.Add(new UserPurchaseEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ShopItemId = shopItemId,
                    PurchasedOn = DateTimeOffset.UtcNow
                });

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });

        return success ? PurchaseResult.Success : PurchaseResult.Failed;
    }

    /// <summary>
    /// Add a reaction to a game night (user can only react once per night, not per game).
    /// If user already reacted to this night, their reaction is moved to the new game.
    /// </summary>
    public async Task<bool> AddReactionAsync(int gameNightGameId, string userId, string emoji, CancellationToken ct = default)
    {
        // Validate emoji is single character
        if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 10)
        {
            return false;
        }

        // Verify user owns this emoji
        var availableEmojis = await GetUserEmojisAsync(userId, ct);
        if (!availableEmojis.Contains(emoji))
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Get the game night ID for this game
        var game = await db.GameNightGames
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameNightGameId, ct);

        if (game is null)
        {
            return false;
        }

        // Find any existing reaction by this user on ANY game from this game night
        var existingReactions = await db.GameResultReactions
            .Where(r => r.UserId == userId)
            .Join(db.GameNightGames.AsNoTracking(),
                  r => r.GameNightGameId,
                  g => g.Id,
                  (r, g) => new { Reaction = r, GameNightId = g.GameNightId })
            .Where(x => x.GameNightId == game.GameNightId)
            .Select(x => x.Reaction)
            .ToListAsync(ct);

        // Check if user is trying to use the same emoji on the same game - toggle it off
        var sameGameSameEmoji = existingReactions.FirstOrDefault(r => r.GameNightGameId == gameNightGameId && r.Emoji == emoji);
        if (sameGameSameEmoji is not null)
        {
            db.GameResultReactions.Remove(sameGameSameEmoji);
            await db.SaveChangesAsync(ct);
            return true;
        }

        // Remove all existing reactions from this user on this night
        if (existingReactions.Count > 0)
        {
            db.GameResultReactions.RemoveRange(existingReactions);
        }

        // Add new reaction
        db.GameResultReactions.Add(new GameResultReactionEntity
        {
            Id = Guid.NewGuid(),
            GameNightGameId = gameNightGameId,
            UserId = userId,
            Emoji = emoji,
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Get reactions for a game, grouped by emoji with counts.
    /// Returns both emoji counts and user-specific markers (keys prefixed with USER_{userId}:).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetGameReactionsAsync(int gameNightGameId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var reactions = await db.GameResultReactions
            .AsNoTracking()
            .Where(r => r.GameNightGameId == gameNightGameId)
            .ToListAsync(ct);

        var result = new Dictionary<string, int>();

        // Group by emoji for public counts
        var grouped = reactions.GroupBy(r => r.Emoji);
        foreach (var g in grouped)
        {
            result[g.Key] = g.Count();
            
            // Add user-specific markers (for tracking which users reacted)
            foreach (var r in g)
            {
                result[$"USER_{r.UserId}:{g.Key}"] = 1;
            }
        }

        return result;
    }

    public async Task<string?> GetUserBadgeRingAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        
        // Get all the user's purchased badge rings
        var badgeRings = await db.UserPurchases
            .Where(up => up.UserId == userId)
            .Join(db.ShopItems,
                up => up.ShopItemId,
                si => si.Id,
                (up, si) => new { si.ItemType, si.Data })
            .Where(x => x.ItemType == "BadgeRing")
            .Select(x => x.Data)
            .ToListAsync(ct);
        
        if (badgeRings.Count == 0)
        {
            return null;
        }
        
        // Return the best ring (platinum > gold > silver > bronze)
        if (badgeRings.Contains("platinum")) return "platinum";
        if (badgeRings.Contains("gold")) return "gold";
        if (badgeRings.Contains("silver")) return "silver";
        if (badgeRings.Contains("bronze")) return "bronze";
            
        return badgeRings.FirstOrDefault();
    }

    private static ShopItem ToDomain(ShopItemEntity entity)
    {
        var emojis = entity.ItemType == "EmojiPack"
            ? entity.Data.Split(',').Select(e => e.Trim()).ToArray()
            : Array.Empty<string>();

        return new ShopItem(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Price,
            entity.ItemType,
            entity.Data,
            entity.MinWinsRequired,
            entity.MembersOnly,
            true, // Always active for public display
            emojis);
    }

    private static ShopItem ToAdminDomain(ShopItemEntity entity)
    {
        var emojis = entity.ItemType == "EmojiPack"
            ? entity.Data.Split(',').Select(e => e.Trim()).ToArray()
            : Array.Empty<string>();

        return new ShopItem(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Price,
            entity.ItemType,
            entity.Data,
            entity.MinWinsRequired,
            entity.MembersOnly,
            entity.IsActive,
            emojis);
    }

    public sealed record ShopItem(
        Guid Id,
        string Name,
        string Description,
        int Price,
        string ItemType,
        string Data,
        int MinWinsRequired,
        bool MembersOnly,
        bool IsActive,
        IReadOnlyList<string> Emojis);
}
