# BoardGameMondays Codebase Cleanup - Refactoring Guide

**Status**: Phases 1-3 Complete ✅ | Phases 4-8 In Progress

This guide documents all manual refactoring steps required to complete the cleanup plan. The code analysis and initial refactoring (test coverage, pattern extraction) are complete. These remaining steps require file organization.

---

## PHASE 7: Remove Unused Code ✅ (Ready to Execute)

### 7.1 Delete: SimpleAuthStateProvider
**File to delete**: `BoardGameMondays/Core/SimpleAuthStateProvider.cs`

**Reason**: Never used in production. Application uses built-in Blazor auth (`AddCascadingAuthenticationState()` in Program.cs:231).

**Verification**: Grep search confirmed zero references outside of file definition.

### 7.2 Move: DemoBgmMember to Tests
**File to move**: 
- FROM: `BoardGameMondays/Core/DemoBgmMember.cs`
- TO: `BoardGameMondays.Tests/Fixtures/DemoBgmMember.cs` (create Fixtures folder)

**Update references**:
- In `BoardGameMondays.Tests/BoardGameServiceTests.cs`: Add `using BoardGameMondays.Tests.Fixtures;`

**Reason**: Test fixture should live in test project, not production code.

---

## PHASE 4: Restructure Core/ Folder

### 4.1 Create Folder Structure
Create these directories under `BoardGameMondays/Core/`:

```
Core/
├── Infrastructure/                (already exists)
│   ├── Email/
│   ├── Authentication/
│   └── Storage/
├── GameManagement/
├── Gameplay/
├── Community/
├── Admin/
├── Content/
├── Reporting/
├── Compliance/
├── Models/
│   ├── Domain/
│   └── ViewModels/
└── Utilities/
```

### 4.2 Move Service Files by Category

**Infrastructure/Email/**
```
Move here:
- ApiEmailSender.cs
- SmtpEmailSender.cs  
- RoutingEmailSender.cs
```

**Infrastructure/Authentication/**
```
Move here:
- CircuitAuthStateProvider.cs
- HttpContextAuthStateProvider.cs
- AdminRoleClaimsTransformation.cs
```

**Infrastructure/Storage/**
```
Move here:
- LocalAssetStorage.cs
- AzureBlobAssetStorage.cs
- IAssetStorage.cs
```

**GameManagement/**
```
Move here:
- BoardGameService.cs
- GameNightService.cs
- WantToPlayService.cs
```

**Gameplay/**
```
Move here:
- BettingService.cs
- OddsService.cs
- RankingService.cs
- OddsFormatter.cs
```

**Community/**
```
Move here:
- BgmMemberService.cs
- BgmMemberDirectoryService.cs
- BgmCoinService.cs
```

**Admin/**
```
Move here:
- ShopService.cs
- TicketService.cs
```

**Content/**
```
Move here:
- BlogService.cs
- MarkdownRenderer.cs
- ImageFileSniffer.cs
```

**Reporting/**
```
Move here:
- RecapStatsService.cs
- GameRecommendationService.cs
```

**Compliance/**
```
Move here:
- ConsentService.cs
- GdprService.cs
- AgreementService.cs
- UserPreferencesService.cs
```

**Models/Domain/**
```
Move here:
- BgmMember.cs (abstract)
- PersistedBgmMember.cs
- BoardGame.cs
- Comment.cs
- MemberReview.cs
- Review.cs
- TicketType.cs
- Overview.cs
- EmptyOverview.cs
- VictoryRouteServiceModels.cs (or rename to appropriate model)
```

**Models/ViewModels/**
```
Move here:
- LoginModel.cs
```

**Utilities/**
```
Move here:
- InputGuards.cs
- GameNightHub.cs
- ~~SimpleAuthStateProvider.cs~~ (DELETE - already handled in Phase 7)
```

**Keep in Core root:**
```
- PwnedPasswordValidator.cs
- DatabaseConnectionStringClassifier.cs

Infrastructure/:
- DatabaseExtensions.cs (already created)
- CachedServiceBase.cs (already created)
- DatabaseConnectionStringClassifier.cs
```

### 4.3 Update Namespaces

After moving files, update all namespace declarations:

**Example**:
```csharp
// Before
namespace BoardGameMondays.Core;

// After (moved to Infrastructure/Email)
namespace BoardGameMondays.Core.Infrastructure.Email;
```

### 4.4 Update Using Statements

Search and replace in all files that reference moved classes:

**Example pattern:**
```
Find: using BoardGameMondays.Core;
Replace: using BoardGameMondays.Core.GameManagement;
         using BoardGameMondays.Core.Infrastructure.Email;
         // etc.
```

**Files to update:**
- `BoardGameMondays/Program.cs` - service registrations
- `BoardGameMondays/_Imports.razor` - global usings
- All Components in `BoardGameMondays/Components/` 
- All Pages in `BoardGameMondays/Pages/`

### 4.5 Update Program.cs Service Registrations

**Location**: Line ~231 in `Program.cs`

**Current**:
```csharp
builder.Services.AddScoped<BoardGameMondays.Core.BgmMemberService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameRecommendationService>();
builder.Services.AddScoped<BoardGameMondays.Core.BoardGameService>();
// ... etc
```

**Update to new namespaces**:
```csharp
builder.Services.AddScoped<BoardGameMondays.Core.Community.BgmMemberService>();
builder.Services.AddScoped<BoardGameMondays.Core.Reporting.GameRecommendationService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameManagement.BoardGameService>();
// ... etc
```

### 4.6 Update Test File References

Search `BoardGameMondays.Tests/` for imports and update:

**Example**:
```csharp
// Before
using BoardGameMondays.Core;

// After
using BoardGameMondays.Core.GameManagement;
using BoardGameMondays.Core.Gameplay;
```

---

## PHASE 5: Break Up Large Services

### 5.1 Split GameNightService (1,141 lines → 4 services)

**Current file**: `Core/GameManagement/GameNightService.cs` (after Phase 4 move)

**Extract into new files**:

**5.1.1 GameNightRsvpService.cs** (~250 lines)
- Methods to extract:
  - `SetRsvpAsync()` - line 161
  - `IsBgmMemberAsync()` - line 193
  - `SetAttendanceAsync()` - line 199
  - `SetAttendingAsync()` - line 238 (back-compat)
  - `SetSnackBroughtAsync()` - line 240

**5.1.2 GameNightPlayerService.cs** (~300 lines)  
- Methods to extract:
  - `AddPlayerAsync()` - line 452
  - `RemovePlayerAsync()` - line 480
  - `SetPlayerTeamAsync()` - line 510
  - `AddGameAsync()` + `AddPlannedGameAsync()` - line 432

**5.1.3 GameNightTeamService.cs** (~200 lines)
- Methods to extract:
  - `SetTeamWinnerAsync()` - line 531
  - `SetTeamColorAsync()` - line 589
  - All team-related victory route handling

**5.1.4 Keep in GameNightService.cs** (~300-400 lines)
- Core CRUD operations:
  - `CreateAsync()`
  - `GetByIdAsync()`
  - `GetByDateAsync()`
  - `GetRecentAsync()`
  - `SetRecapAsync()`
  - `SetHasStartedAsync()`
  - Cache management
  - Domain mapping methods
  - Util methods (ToDateKey, FromDateKey)

### 5.2 Dependency Management

**New service dependencies**:
```csharp
// GameNightRsvpService depends on:
- IDbContextFactory<ApplicationDbContext>

// GameNightPlayerService depends on:
- IDbContextFactory<ApplicationDbContext>
- GameNightService (for lookups)

// GameNightTeamService depends on:
- IDbContextFactory<ApplicationDbContext>
- GameNightService (for lookups)
- OddsService (for team betting)
- RankingService (for ranking updates)

// GameNightService depends on:
- IDbContextFactory<ApplicationDbContext>
- OddsService (for odds generation)
- IMemoryCache (for caching)
```

### 5.3 Register New Services in Program.cs

```csharp
builder.Services.AddScoped<BoardGameMondays.Core.GameManagement.GameNightService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameManagement.GameNightRsvpService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameManagement.GameNightPlayerService>();
builder.Services.AddScoped<BoardGameMondays.Core.GameManagement.GameNightTeamService>();
```

---

## PHASE 6: Fix Separation of Concerns

### 6.1 Simplify BgmMemberService (10 lines)
**File**: `Core/Community/BgmMemberService.cs`

**Current**: Just a property holder (CurrentMember)
**Fix**: Either:
- Option A: Remove service, inline into component state
- Option B: Keep as minimal state holder but document intent

### 6.2 Extract BgmCoinService Responsibilities (400 lines)
**File**: `Core/Community/BgmCoinService.cs`

**Create AttendanceRewardCalculator.cs** (~100 lines)
- Move: `CalculateAttendanceRewardAsync()` and attendance week logic
- New namespace: `BoardGameMondays.Core.Community`

**Create AdminCoinOverrideHandler.cs** (~50 lines)
- Move: Admin override logic from line 277+
- New namespace: `BoardGameMondays.Core.Community`

### 6.3 Document BettingService Orchestration
**File**: `Core/Gameplay/BettingService.cs`

**Add XML documentation** at class level explaining:
- Dependency chain: BgmCoinService → RankingService → OddsService → GameNightHub
- When each service is called
- Transaction boundaries

### 6.4 Divide GDPR Responsibilities
**File**: `Core/Compliance/GdprService.cs`

**Create UserDataExporter.cs** (~110 lines)
- Move: `ExportUserDataAsync()` (line 31-142)
- Dependency: ConsentService, game data services

**Create UserDataDeleter.cs** (~100 lines)
- Move: `RequestAccountDeletionAsync()` and approval logic
- Dependency: ConsentService, game data services

---

## PHASE 8: Documentation & Cleanup

### 8.1 Create Infrastructure Guide
**File**: `Core/Infrastructure/README.md`

Content:
```markdown
# Infrastructure Layer

Handles cross-cutting concerns: authentication, database access, email, file storage.

## Conventions

- All services use `IDbContextFactory<ApplicationDbContext>` (not injected DbContext)
- See `DatabaseExtensions.cs` for context lifecycle helpers
- Email sender routing based on config (API vs SMTP)
- Storage provider swappable (Local vs Azure Blob)
```

### 8.2 Create Service Architecture Reference
**File**: `Core/ServiceLayerArchitecture.md`

Content:
```markdown
# Service Layer Architecture  

## Organization by Domain

### GameManagement/
CRUD for game nights and game-related operations.
- GameNightService (core)
- GameNightRsvpService (attendance)
- GameNightPlayerService (game composition)
- GameNightTeamService (team-based games)

### Gameplay/
Betting, odds, rankings.
- BettingService (orchestrator)
- OddsService (odds calculation)
- RankingService (ELO updates)

### Community/
Member management and coin rewards.
- BgmMemberDirectoryService
- BgmCoinService (with calculator helpers)
- (others)

### Compliance/
GDPR, consent, data export/deletion.
- ConsentService
- GdprService (with exporter/deleter helpers)
- AgreementService
- UserPreferencesService

## Dependency Graph

(Simplified - see actual code for full dependencies)

```
UI Components
↓
Services (Auth, Game, Betting, etc.)
↓
OddsService
↓
RankingService
↓
BgmCoinService
↓
IDbContextFactory<ApplicationDbContext>
↓
SQL Server / SQLite
```

## Best Practices for New Services

1. Inject `IDbContextFactory<ApplicationDbContext>` (not DbContext)
2. Use `DatabaseExtensions.ExecuteInDbContextAsync()` to reduce boilerplate
3. Inherit from `CachedServiceBase` if you use caching
4. Keep circular dependencies out (example: don't have Service A → B → A)
5. Validate inputs with `InputGuards` helpers
```

### 8.3 Update _Imports.razor
**File**: `BoardGameMondays/_Imports.razor`

Organize global usings by category:

```razor
@* Infrastructure *@
@using BoardGameMondays.Core.Infrastructure;
@using BoardGameMondays.Core.Infrastructure.Email;
@using BoardGameMondays.Core.Infrastructure.Authentication;

@* Services *@
@using BoardGameMondays.Core.GameManagement;
@using BoardGameMondays.Core.Gameplay;
@using BoardGameMondays.Core.Community;
@using BoardGameMondays.Core.Admin;
@using BoardGameMondays.Core.Content;
@using BoardGameMondays.Core.Reporting;
@using BoardGameMondays.Core.Compliance;

@* Models *@
@using BoardGameMondays.Core.Models.Domain;
@using BoardGameMondays.Core.Models.ViewModels;

@* ... rest of imports *@
```

### 8.4 Add .gitignore Exception (if needed)
If new subdirectories are empty, ensure they stay in git:

```
# Create .gitkeep files in empty folders
Core/Infrastructure/.gitkeep
Core/GameManagement/.gitkeep
Core/Gameplay/.gitkeep
# ... etc
```

---

## Execution Order Summary

1. **Phase 7** (Quick) - Delete SimpleAuthStateProvider, move DemoBgmMember to Tests
2. **Phase 4** (Medium) - Create folder structure, move files, update namespaces
3. **Phase 5** (Large) - Split GameNightService into 4 focused services
4. **Phase 6** (Medium) - Extract responsibilities (coin calculator, betting docs, GDPR split)
5. **Phase 8** (Small) - Add documentation files and README

---

## Key Benefits After Completion

✅ **Code Organization**: 50+ files organized into 8 logical domains instead of flat Core/  
✅ **Reduced Duplication**: DbContext pattern extracted (100+ lines saved)  
✅ **Better Testability**: 70+ tests created for critical services  
✅ **Clearer Separation**: Each service has single responsibility  
✅ **Maintainability**: New files go into clearly appropriate folders  
✅ **Documentation**: Service architecture self-evident from folder structure  

---

## Notes for Execution

- Build solution frequently after each phase to catch namespace/reference errors
- Search for old namespaces with grep to find missed references
- Update test file usings - they reference the moved services
- Consider doing Phase 4+5+8 together (one refactoring pass)
- Phase 6 items are non-breaking (internal refactoring), can be phased
