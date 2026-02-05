# Entity Modification Checklist

**Use this checklist every time you modify an entity in `Data/Entities/`**

## Before Modifying an Entity

- [ ] Understand what change you're making (add property, remove, rename, change type, etc.)
- [ ] Identify which entity file needs modification
- [ ] Have your migration ready to run once you finish

## After Modifying an Entity

### Step 1: Create the Migration
```
cd BoardGameMondays
dotnet ef migrations add [DescriptiveName]
```

Change `[DescriptiveName]` to something like:
- `AddGameNightVenue` (adding property)
- `RemoveOldTicketField` (removing property)
- `RenamePlayerTeamName` (renaming property)
- `UpdateMemberMaxLength` (updating constraints)

### Step 2: Review Generated Migration
- [ ] Open `Migrations/[timestamp]_[DescriptiveName].cs`
- [ ] Verify `Up()` method has the correct ADD/DROP/ALTER statements
- [ ] Verify `Down()` method properly reverses the changes
- [ ] Check that column types match your entity property types:
  - `bool` → `INTEGER` (SQLite) or `bit` (SQL Server)
  - `string` → `TEXT` (SQLite) or `nvarchar` (SQL Server)
  - `int` → `INTEGER` (SQLite) or `int` (SQL Server)
  - `DateTimeOffset` → `INTEGER` (or `TEXT` if using ticks conversion)

### Step 3: Test the Migration

**Option A: Run the test suite**
```
dotnet test --filter "MigrationTests"
```

**Option B: Run validation script**
```
# On Windows PowerShell:
.\validate-migrations.ps1

# On macOS/Linux bash:
./validate-migrations.sh
```

- [ ] All migration tests pass
- [ ] No "pending changes" warning

### Step 4: Commit the Changes
```
git status  # Verify you see modified entity AND migration files

git add "Data/Entities/[YourEntity].cs"
git add "Migrations/[timestamp]_[DescriptiveName].*"
git add "Migrations/ApplicationDbContextModelSnapshot.cs"

git commit -m "Add [field] to [Entity]

- Updated [Entity].cs with new property
- Created migration to add column to database
- Verified by running migration tests"
```

## Common Entity Modification Patterns

### Pattern 1: Adding a New Property

**File**: `Data/Entities/GameNightEntity.cs`
```csharp
public sealed class GameNightEntity
{
    // ... existing properties ...
    
    [MaxLength(500)]
    public string? Venue { get; set; }  // NEW PROPERTY
}
```

**Command**: 
```bash
dotnet ef migrations add AddGameNightVenue
```

**Expected migration change**: Adds a TEXT column with max length 500

---

### Pattern 2: Making a Property Required

**Before**:
```csharp
public string? Description { get; set; }
```

**After**:
```csharp
[Required]
public string Description { get; set; } = string.Empty;
```

**Command**: 
```bash
dotnet ef migrations add MakeDescriptionRequired
```

**Expected migration change**: Alters column to NOT NULL (with default empty string for existing rows)

---

### Pattern 3: Changing Property Type

**Before**:
```csharp
public int Rating { get; set; }
```

**After**:
```csharp
public decimal Rating { get; set; }  // More precise for ratings
```

**Command**: 
```bash
dotnet ef migrations add ChangeRatingToDecimal
```

**Expected migration change**: Alters column type from INT to REAL/DECIMAL

---

### Pattern 4: Adding an Index in OnModelCreating

**File**: `Data/ApplicationDbContext.cs`
```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    // ... existing config ...
    
    // NEW: Add index for frequently searched fields
    builder.Entity<MemberEntity>()
        .HasIndex(x => x.Email)
        .IsUnique();
}
```

**Command**: 
```bash
dotnet ef migrations add AddEmailUniqueIndex
```

**Expected migration change**: Creates a UNIQUE INDEX on the Email column

---

## Migration Naming Convention

Use descriptive names that explain WHAT changed:

✅ **Good names**:
- `AddGameNightVenue`
- `RemoveTicketPriority`
- `MakePlayerNameRequired`
- `AddUniqueIndexOnSlug`
- `UpdateGameScorePrecision`
- `AddDefaultEloRating`
- `CreateGameNightRsvpTable`

❌ **Bad names**:
- `Update`
- `Migration`
- `Pending`
- `Fix`
- `Temp`

---

## Troubleshooting

### ❌ Error: "A migration named '[name]' already exists"

**Cause**: You're trying to create a migration file that already exists.

**Fix**:
```bash
git checkout Migrations/  # Revert any migrations
dotnet ef migrations add [DifferentName]
```

---

### ❌ Error: "Unable to create an object of type 'ApplicationDbContext'"

**Cause**: Your `OnModelCreating` configuration has an error.

**Fix**:
1. Check the detailed error message for which entity/configuration is wrong
2. Verify the entity property exists and matches the configuration
3. Make sure foreign keys reference actual entities
4. Run `dotnet build` to see compile errors

---

### ❌ Error: "This MigrationOperation cannot be executed by this provider"

**Cause**: Your migration uses SQL Server syntax that SQLite doesn't support (or vice versa).

**Fix**: 
1. Edit the `.cs` migration file
2. Replace vendor-specific syntax with EF-based configurations
3. Use `migrationBuilder` methods instead of raw SQL

Example:
```csharp
// ❌ SQL Server specific
migrationBuilder.Sql("ALTER TABLE Games ADD CONSTRAINT DF_Active DEFAULT 1 FOR IsActive");

// ✅ Works everywhere
migrationBuilder.AddColumn<bool>(
    name: "IsActive",
    table: "Games",
    type: "INTEGER",
    nullable: false,
    defaultValue: true);
```

---

## Quick Checklist Template

Copy this for each entity change:

```
Entity Modified: _______________
Property Changed: _______________
Change Type: [ ] Add  [ ] Remove  [ ] Modify Type  [ ] Update Constraint  [ ] Index  [ ] Relationship

Migration Created: _______________
🏁 Tests Passed: [ ] Yes [ ] No
🏁 File Changes Staged: [ ] Yes [ ] No
🏁 Commit Created: [ ] Yes [ ] No
```

---

## Questions?

See `MIGRATIONS_GUIDE.md` for detailed information on:
- Step-by-step workflows
- Common issues and solutions
- Git pre-commit hooks
- CI/CD integration
