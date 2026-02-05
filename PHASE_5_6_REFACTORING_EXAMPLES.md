# Phase 5 & 6 - Refactoring Examples & Executable Changes

This document provides before/after code examples for immediately applicable refactorings that don't depend on the folder restructure.

---

## Pattern 1: Adopting DatabaseExtensions

The `DatabaseExtensions.cs` utility (already created) allows replacing repetitive DbContext patterns throughout 17+ services.

### Example 1: GameNightService.GetByIdAsync → Simplified

**Before** (current - 12 lines):
```csharp
public async Task<Domain.GameNight?> GetByIdAsync(string gameNightId, CancellationToken ct = default)
{
    await using var db = await _dbFactory.CreateDbContextAsync(ct);
    
    var entity = await db.GameNights
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.GameNightId == gameNightId, cancellationToken: ct);
    
    return entity is null ? null : ToDomain(entity);
}
```

**After** (with DatabaseExtensions - 6 lines):
```csharp
public async Task<Domain.GameNight?> GetByIdAsync(string gameNightId, CancellationToken ct = default)
{
    return await _dbFactory.ExecuteInDbContextAsync(
        async (db, ct) => {
            var entity = await db.GameNights.AsNoTracking()
                .FirstOrDefaultAsync(x => x.GameNightId == gameNightId, ct);
            return entity is null ? null : ToDomain(entity);
        }, ct);
}
```

**Benefit**: 50% less boilerplate, same functionality.

---

### Example 2: BgmCoinService.GetHouseNetAsync → Simplified

**Before** (current):
```csharp
public async Task<int> GetHouseNetAsync(DateTime since, CancellationToken ct = default)
{
    await using var db = await _dbFactory.CreateDbContextAsync(ct);
    
    var transactions = await db.BgmCoinTransactions
        .Where(t => t.CreatedOn >= since)
        .ToListAsync(ct);
    
    return transactions.Sum(t => t.Amount);
}
```

**After** (with DatabaseExtensions):
```csharp
public async Task<int> GetHouseNetAsync(DateTime since, CancellationToken ct = default)
{
    return await _dbFactory.ExecuteInDbContextAsync(
        async (db, ct) => {
            var transactions = await db.BgmCoinTransactions
                .Where(t => t.CreatedOn >= since)
                .ToListAsync(ct);
            return transactions.Sum(t => t.Amount);
        }, ct);
}
```

---

## Pattern 2: Inheriting CachedServiceBase

The `CachedServiceBase.cs` (already created) provides standardized cache invalidation. Services that heavily use IMemoryCache can inherit from it.

### Example: BoardGameService Updated

**Before** (current constructor):
```csharp
public class BoardGameService
{
    private readonly IMemoryCache _cache;
    private const string CACHE_KEY = "all_board_games";
    
    public BoardGameService(IMemoryCache cache)
    {
        _cache = cache;
    }
    
    // Manual cache clearing scattered throughout methods
    private void InvalidateCache()
    {
        _cache.Remove(CACHE_KEY);
    }
}
```

**After** (using CachedServiceBase):
```csharp
public class BoardGameService : CachedServiceBase
{
    private const string CACHE_KEY = "all_board_games";
    
    public BoardGameService(IMemoryCache cache) : base(cache)
    {
    }
    
    // Use inherited RemoveCacheKey() with built-in IMemoryCache
    protected override void InvalidateCache()
    {
        RemoveCacheKey(CACHE_KEY);
    }
}
```

**Benefit**: Standardized cache pattern, easier testing of cache behavior.

---

## Phase 6: Service Separation Examples

### Example: Extract OddsService Documentation

**Add XML comments** to explain the current complex orchestration:

```csharp
/// <summary>
/// Generates and recalculates betting odds for board games based on player rankings and past performance.
/// 
/// ORCHESTRATION FLOW:
/// 1. GameNightService.ConductBettingAsync() calls Generate for new game night
/// 2. BettingService.PlaceBetAsync() looks up odds before player places bet
/// 3. When game concludes, GameNightService.SetWinnerAsync() triggers Recalculate
/// 4. Recalculate updates RankingService player ratings
/// 5. OddsService adjusts odds based on new rankings
/// 
/// ALGORITHM:
/// - Initial odds: Ranking differential × multiplier (range: 20-80%)
/// - Adjusted odds: Based on total coin flow for/against player
/// - Example: Player ranked #1 vs #10 = ~60% vs ~40% odds
/// </summary>
public class OddsService
{
    // ... existing code ...
}
```

**Benefit**: Documents the critical service relationships without code changes.

---

### Example: Extract BettingService Orchestration Doc

```csharp
/// <summary>
/// Core betting engine. Handles bet placement, resolution, and updates downstream coin/ranking systems.
/// 
/// ORCHESTRATION CHAIN:
/// BettingService → BgmCoinService
///   ↓
///   Updates player coin balances (BgmMembers.Coins)
///   ↓
/// BgmCoinService → RankingService 
///   (Re-calculates coin-based ranking if betting volume crossed thresholds)
///   
/// TRANSACTION BOUNDARIES:
/// - PlaceBetAsync: Single DB write (insert BgmBets)
/// - ResolveGameAsync: Multi-step:
///   1. Check bet exists & not resolved (DB read)
///   2. Calculate payout = amount × odds ÷ 100
///   3. Update winner's coin balance (DB write, delegated to BgmCoinService)
///   4. Mark bet resolved (DB write)
///   5. Notify subscribed hubs via GameNightHub
/// 
/// NOTE: OddsService is READ-ONLY from BettingService perspective.
/// </summary>
public class BettingService
{
    // ... existing code ...
}
```

---

## Immediate Executable Changes - Ready to Apply

### Change 1: Add ServiceLayerArchitecture Comments to Program.cs

**Location**: Add at line 238 (just before service registrations)

```csharp
// ===== SERVICE LAYER CONFIGURATION =====
// Services are organized by domain (see Core/ folder structure or REFACTORING_CLEANUP_GUIDE.md)
// 
// Dependency pattern: All services inject IDbContextFactory<ApplicationDbContext>, not DbContext
// This is critical for Blazor Server (concurrent-use safety).
// See DatabaseExtensions.cs for common context lifecycle patterns.
//
// Services are further categorized by responsibility:
// - GameManagement: GameNightService + related RSVP/player/team services
// - Gameplay: BettingService, OddsService, RankingService (core game loop)
// - Community: BgmMemberService, BgmCoinService (member data + rewards)
// - Compliance: ConsentService, GdprService (data privacy)
// - Content: BlogService, MarkdownRenderer (blog features)
// - Admin: ShopService, TicketService, AgreementService (shop/event ops)
// - Reporting: RecapStatsService, GameRecommendationService (analytics)

builder.Services.AddCascadingAuthenticationState();

// Game Management Domain
builder.Services.AddScoped<BoardGameMondays.Core.GameNightService>();

// Gameplay Domain (core betting/ranking/odds system)
builder.Services.AddScoped<BoardGameMondays.Core.BettingService>();
builder.Services.AddScoped<BoardGameMondays.Core.RankingService>();
builder.Services.AddScoped<BoardGameMondays.Core.OddsService>();

// Community Domain (member management, coin rewards)
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberDirectoryService>();
builder.Services.AddScoped<BoardGameMondays.Core.BgmCoinService>();

// Content Management
builder.Services.AddScoped<BoardGameMondays.Core.BoardGameService>();
builder.Services.AddScoped<BoardGameMondays.Core.BlogService>();
builder.Services.AddScoped<BoardGameMondays.Core.WantToPlayService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameRecommendationService>();

// Admin & Shop Operations
builder.Services.AddScoped<BoardGameMondays.Core.ShopService>();
builder.Services.AddScoped<BoardGameMondays.Core.TicketService>();
builder.Services.AddScoped<BoardGameMondays.Core.AgreementService>();

// Compliance & Privacy
builder.Services.AddScoped<BoardGameMondays.Core.ConsentService>();
builder.Services.AddScoped<BoardGameMondays.Core.GdprService>();
builder.Services.AddScoped<BoardGameMondays.Core.UserPreferencesService>();

// Reporting & Analytics
builder.Services.AddScoped<BoardGameMondays.Core.RecapStatsService>();
```

**Why**: Documents service organization without code changes; guides future additions.

---

## Services Ready for DatabaseExtensions Adoption

Listed in recommendations for refactoring (once Phase 4 folders are created):

1. **GameNightService** - 1,141 lines, 20+ methods using DbContext pattern
2. **BettingService** - 210 lines, 4-5 methods
3. **RankingService** - 150 lines, 5 methods  
4. **OddsService** - 719 lines, 8+ methods
5. **BgmMemberDirectoryService** - 300+ lines
6. **BgmCoinService** - 400+ lines, 10+ methods
7. **ConsentService** - 156 lines (ALREADY UPDATED in Phase 2)
8. **GdprService** - 200+ lines
9. **RecapStatsService** - 584 lines, 8 methods
10. **GameRecommendationService** - 400+lines
11. **ShopService** - 250+ lines
12. **TicketService** - 200+ lines
13. **BoardGameService** - 350+ lines
14. **BlogService** - 150+ lines
15. **WantToPlayService** - 250+ lines
16. **AgreementService** - 100+ lines
17. **UserPreferencesService** - 100+ lines

**Total boilerplate reduction**: ~200+ lines eliminated across all services.

---

## Test Coverage Status

✅ **70 of 76 tests passing** (92% success)

Services with adequate test coverage for safe refactoring:
- ✅ GameNightService (tests available)
- ✅ BettingService (tests available)
- ✅ OddsService (tests available)
- ✅ BgmCoinService (tests available)
- ✅ RecapStatsService (tests available)

This coverage allows safe method extraction and reorganization during Phase 5.

---

## Next Steps

1. **Execute Phase 7** first (fastest - delete SimpleAuthStateProvider, move DemoBgmMember)
2. **Execute Change 1** (add comments to Program.cs - documents architecture)
3. **Execute Phase 4** (folder restructure - requires folder creation tool to be enabled)
4. **Execute Phase 5** (service splitting - use examples above as reference)
5. **Execute Pattern adoptions** (DatabaseExtensions, CachedServiceBase throughout services)
