# Entity Framework Migrations Guide

## Quick Summary

**Every time you modify an entity in `Data/Entities/`, you MUST create a migration before committing.**

```bash
# 1. Modify an entity file
# 2. Create a migration
dotnet ef migrations add DescriptiveNameOfChange

# 3. Review the generated migration
# 4. Run tests
dotnet test

# 5. Commit both the entity AND migration files
```

---

## What Requires a Migration?

| Change | Example | Migration Command |
|--------|---------|-------------------|
| **Add property** | Add `IsActive` to `MemberEntity` | `dotnet ef migrations add AddMemberIsActive` |
| **Remove property** | Remove `OldField` from `TicketEntity` | `dotnet ef migrations add RemoveTicketOldField` |
| **Change property type** | Change `Score` from `int` to `long` | `dotnet ef migrations add ChangeScoreType` |
| **Add constraint** | Add `[Required]` to a property | `dotnet ef migrations add AddRequiredConstraint` |
| **Add/modify index** | Add `HasIndex()` in `OnModelCreating` | `dotnet ef migrations add AddIndexName` |
| **Add column default** | Add default value in `OnModelCreating` | `dotnet ef migrations add AddColumnDefault` |
| **Modify relationship** | Change foreign key config | `dotnet ef migrations add UpdateRelationship` |

---

## Step-by-Step Workflow

### 1. Modify an Entity
```csharp
// Data/Entities/GameNightEntity.cs
public sealed class GameNightEntity
{
    // ... existing properties ...
    
    // NEW: Add a property
    [MaxLength(500)]
    public string? Venue { get; set; }
}
```

### 2. Create a Migration
```bash
cd BoardGameMondays
dotnet ef migrations add AddGameNightVenue
```

✅ This generates:
- `Migrations/20260205XXXXXX_AddGameNightVenue.cs` (the actual migration)
- `Migrations/20260205XXXXXX_AddGameNightVenue.Designer.cs` (metadata)
- Updates `Migrations/ApplicationDbContextModelSnapshot.cs` (full model state)

### 3. Review the Migration
Open `Migrations/20260205XXXXXX_AddGameNightVenue.cs` and verify:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Should add the Venue column
    migrationBuilder.AddColumn<string>(
        name: "Venue",
        table: "GameNights",
        type: "TEXT",
        maxLength: 500,
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Rollback: remove the Venue column
    migrationBuilder.DropColumn(
        name: "Venue",
        table: "GameNights");
}
```

### 4. Run Tests
```bash
# Verify the migration is valid
dotnet test

# Run only migration tests
dotnet test --filter "MigrationTests"
```

### 5. Commit Everything
```bash
git add BoardGameMondays/Data/Entities/GameNightEntity.cs
git add "BoardGameMondays/Migrations/20260205*_AddGameNightVenue.*"
git add "BoardGameMondays/Migrations/ApplicationDbContextModelSnapshot.cs"
git commit -m "Add Venue field to GameNightEntity"
```

---

## Common Issues

### ❌ Issue: "The model for context 'ApplicationDbContext' has pending changes"

**Cause**: You modified an entity but didn't create a migration.

**Fix**:
```bash
dotnet ef migrations add DescribePendingChange
dotnet test  # Verify it works
git add Migrations/*
```

### ❌ Issue: Empty Migration Generated

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Empty - no changes!
}
```

**Cause**: No actual model changes detected (you may have reverted a change).

**Fix**: Delete the migration file and regenerate.
```bash
dotnet ef migrations remove
# ... make actual changes ...
dotnet ef migrations add CorrectMigration
```

### ❌ Issue: Migration Won't Apply to Database

**Cause**: The migration SQL syntax doesn't match your database (SQLite vs SQL Server).

**Fix**: Edit the migration to use correct syntax:
```csharp
// ❌ SQL Server syntax only
ALTER TABLE Games ADD CONSTRAINT DF_Score DEFAULT 0 FOR Score;

// ✅ Works on both SQLite and SQL Server
migrationBuilder.AddColumn<int>(..., defaultValue: 0);
```

### ✅ Tip: Test Migrations Locally First

```bash
# Check what migrations would be applied without applying them
dotnet ef migrations list

# See SQL that would be executed
dotnet ef migrations script --idempotent > migrations.sql
```

---

## Git Workflow: Preventing Migration Issues

### Add a Git Pre-Commit Hook
Create `.git/hooks/pre-commit`:

```bash
#!/bin/bash
# Prevent committing modified entities without corresponding migrations

MODIFIED_ENTITIES=$(git diff --cached --name-only | grep "Data/Entities/.*\.cs$")
MODIFIED_MIGRATIONS=$(git diff --cached --name-only | grep "Migrations/.*\.cs$")

if [[ ! -z "$MODIFIED_ENTITIES" ]] && [[ -z "$MODIFIED_MIGRATIONS" ]]; then
    echo "❌ ERROR: You modified entity files but have no migration changes!"
    echo "   Modified entities: $MODIFIED_ENTITIES"
    echo "   Run: dotnet ef migrations add [MigrationName]"
    exit 1
fi

exit 0
```

Make it executable:
```bash
chmod +x .git/hooks/pre-commit
```

---

## CI/CD Integration

Add to your build pipeline:

```yaml
# .github/workflows/build.yml
- name: Run Migration Tests
  run: dotnet test --filter "MigrationTests" --logger "trx"
  
- name: Check for Pending Migrations
  run: dotnet ef migrations list --no-build
```

---

## Good Commit Messages for Migrations

✅ **Good**:
```
Add Venue field to GameNightEntity

- Adds nullable Venue column to GameNights table
- Max length: 500 characters
- Allows recording where game night takes place
```

❌ **Bad**:
```
Update database
```

---

## FAQ

**Q: Can I skip migrations for development?**
A: No. Even in development, migrations keep your schema synchronized. This catches issues early.

**Q: What if I make 3 entity changes?**
A: Create ONE migration that captures all 3 changes:
```bash
# Modify 3 entities
dotnet ef migrations add UpdateMultipleEntities
# This single migration handles all 3 changes
```

**Q: Can I delete old migrations?**
A: No. Migration history must be preserved. Old migrations are applied in order during database setup.

**Q: What if production database and migrations are out of sync?**
A: Use the `EnsureSqlServer*` methods in `Program.cs` to patch missing columns as a defensive guard.

---

## Quick Reference

| Command | Purpose |
|---------|---------|
| `dotnet ef migrations add Name` | Create a new migration |
| `dotnet ef migrations list` | List all migrations |
| `dotnet ef migrations remove` | Remove the **last** migration (design-time only) |
| `dotnet ef database update` | Apply all pending migrations |
| `dotnet ef database update MigrationName` | Apply up to specific migration |
| `dotnet test --filter "MigrationTests"` | Run migration validation tests |

---

## Prevention Checklist

Before you commit:
- [ ] I modified Data/Entities/*.cs files
- [ ] I ran `dotnet ef migrations add [Name]`
- [ ] I reviewed the generated migration file
- [ ] I ran `dotnet test` (all pass, including MigrationTests)
- [ ] Migrations are in my git staging area
- [ ] Entities and migrations are in the same commit
