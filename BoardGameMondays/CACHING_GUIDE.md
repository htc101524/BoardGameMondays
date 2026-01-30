# Caching Strategy for BoardGameMondays

## The Problem
Each Blazor Server circuit makes many SQL calls per user session. For a board game site:
- Every page load fetches board games, members, game nights
- Same data is re-queried across multiple components
- Read-heavy workloads (viewing >> editing)

## How Social Media Sites Handle This

### Multi-Tier Caching Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                         CDN/Edge Layer                           │
│  (Cloudflare, Azure Front Door, Akamai)                          │
│  → Static assets, public API responses                           │
└──────────────────────────────────────────────────────────────────┘
                                ↓
┌──────────────────────────────────────────────────────────────────┐
│                    Distributed Cache Layer                        │
│  (Redis, Memcached)                                               │
│  → Session data, user profiles, hot content                       │
│  → Shared across all app servers                                  │
└──────────────────────────────────────────────────────────────────┘
                                ↓
┌──────────────────────────────────────────────────────────────────┐
│                    Application Memory Cache                       │
│  (IMemoryCache in .NET)                                           │
│  → Per-server, fastest access                                     │
│  → Configuration, lookup tables, computed data                    │
└──────────────────────────────────────────────────────────────────┘
                                ↓
┌──────────────────────────────────────────────────────────────────┐
│                        Database Layer                             │
│  (SQL Server, Cosmos DB)                                          │
│  → Source of truth, writes always go here                         │
│  → Query result caching (automatic in some DBs)                   │
└──────────────────────────────────────────────────────────────────┘
```

### Key Patterns Used by Twitter/Reddit/Facebook

1. **Write-Through Cache**: Writes go to cache + DB together
2. **Read-Through Cache**: Check cache first, fetch from DB on miss
3. **Event-Driven Invalidation**: Publish events when data changes
4. **Time-Based Expiration**: Stale data acceptable for some period
5. **Cache-Aside Pattern**: Application manages cache explicitly

## Recommended Implementation for BoardGameMondays

### Phase 1: Memory Cache for Static/Semi-Static Data (Simple)

Add caching to these high-read, low-write services:

| Service | Data | Cache Duration | Invalidation |
|---------|------|----------------|--------------|
| BoardGameService | Games list | 5 min | On add/update/delete |
| BgmMemberDirectoryService | Members | 15 min | On add/update |
| GameNightService | Past game nights | 10 min | On result change |
| ShopService | Shop items | 5 min | On item change |

### Phase 2: Distributed Cache (Redis) for Scaling

When you have multiple app servers:

```csharp
// Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "BoardGameMondays:";
});
```

### Phase 3: Response Caching for Public Pages

```csharp
// On public pages that don't need real-time data
[OutputCache(Duration = 60)] // ASP.NET Core 7+
public IActionResult GamesLibrary() => View();
```

## Implementation Examples

### Simple Pattern: Add Caching to Existing Service

```csharp
public sealed class BoardGameService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    
    // Cache key constants
    private const string AllGamesCacheKey = "games:all";
    private const string GameCacheKeyPrefix = "games:";
    
    public BoardGameService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }
    
    public async Task<IReadOnlyList<BoardGame>> GetAllAsync(CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(AllGamesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var games = await db.Games
                .AsNoTracking()
                .Include(g => g.VictoryRoutes)
                .ThenInclude(r => r.Options)
                .Include(g => g.Reviews)
                .ThenInclude(r => r.Reviewer)
                .OrderBy(g => g.Name)
                .ToListAsync(ct);

            return games.Select(ToDomain).ToArray();
        }) ?? [];
    }
    
    public async Task<BoardGame> AddGameAsync(/* params */)
    {
        // ... create game ...
        
        // Invalidate cache after write
        InvalidateCache();
        
        return newGame;
    }
    
    private void InvalidateCache()
    {
        _cache.Remove(AllGamesCacheKey);
        // The Changed event already exists - use it for cross-component invalidation
        Changed?.Invoke();
    }
}
```

### Advanced Pattern: Cache with Automatic Refresh

```csharp
// For data that should stay fresh but not block users
public async Task<IReadOnlyList<BoardGame>> GetAllAsync(CancellationToken ct = default)
{
    if (_cache.TryGetValue(AllGamesCacheKey, out IReadOnlyList<BoardGame>? cached))
    {
        // Return cached data immediately
        // Optionally refresh in background if stale
        return cached!;
    }
    
    // Cache miss - fetch and cache
    var games = await FetchGamesFromDbAsync(ct);
    
    _cache.Set(AllGamesCacheKey, games, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        SlidingExpiration = TimeSpan.FromMinutes(2) // Extend if accessed
    });
    
    return games;
}
```

## What NOT to Cache

1. **User authentication state** - Use circuit state instead
2. **Real-time betting odds** - Needs to be live
3. **Active game night being edited** - Stale data = bad UX
4. **Write operations** - Always hit the database
5. **User-specific mutable data** - Coins, bets (unless using distributed cache with user keys)

## Measuring Impact

Add logging to track cache effectiveness:

```csharp
public async Task<IReadOnlyList<BoardGame>> GetAllAsync(CancellationToken ct = default)
{
    if (_cache.TryGetValue(AllGamesCacheKey, out IReadOnlyList<BoardGame>? cached))
    {
        _logger.LogDebug("Cache HIT for {Key}", AllGamesCacheKey);
        return cached!;
    }
    
    _logger.LogDebug("Cache MISS for {Key}", AllGamesCacheKey);
    // ... fetch from DB
}
```

## Quick Wins for BoardGameMondays

1. **BoardGameService.GetAllAsync** - Called on every games page
2. **BgmMemberDirectoryService.GetAll** - Called for member dropdowns
3. **GameNightService.GetAllMembersAsync** - Used in game result forms
4. **UserPreferencesService.GetOddsDisplayFormatAsync** - Called per-user per-render

Start with these 4 methods and measure the SQL call reduction.
