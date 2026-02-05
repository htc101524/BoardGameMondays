# BoardGameMondays
A Blazor website for the board game Mondays group
## Development Setup

### Prerequisites
- .NET 10.0 SDK or later
- Visual Studio Code or Visual Studio

### Running the Application

```bash
dotnet run --project BoardGameMondays
```

The app will start at `https://localhost:5001`

### Running Tests

```bash
# Run all tests
dotnet test

# Run only migration tests (validates no pending model changes)
dotnet test --filter "MigrationTests"

# Run with coverage
dotnet test /p:CollectCoverage=true
```

## Entity Framework Migrations

**IMPORTANT**: Every time you modify an entity in `Data/Entities/`, you must create a migration.

### Quick Start
```bash
# 1. Modify your entity
# 2. Create migration
dotnet ef migrations add DescriptiveNameOfChange

# 3. Run tests to validate
dotnet test --filter "MigrationTests"

# 4. Commit entity AND migration files
```

### Validation Scripts

**Windows**:
```powershell
.\validate-migrations.ps1
```

**macOS/Linux**:
```bash
./validate-migrations.sh
```

For detailed migration guidance, see:
- `MIGRATIONS_GUIDE.md` - Complete migration reference
- `ENTITY_MODIFICATION_CHECKLIST.md` - Developer checklist

## Common Commands

| Command | Purpose |
|---------|---------|
| `dotnet build` | Build the solution |
| `dotnet test` | Run all tests |
| `dotnet ef migrations add Name` | Create new migration |
| `dotnet ef migrations list` | List all migrations |
| `dotnet ef database update` | Apply pending migrations |

## Troubleshooting

### "The model for context has pending changes"
You modified an entity but forgot to create a migration.
```bash
dotnet ef migrations add [YourMigrationName]
dotnet test
```

### Migration tests fail locally but pass in CI
Run tests with `Release` configuration:
```bash
dotnet test --configuration Release
```

## Project Structure

- `BoardGameMondays/` - Main Blazor application
  - `Components/` - Razor components and pages
  - `Core/` - Business logic and services
  - `Data/` - Entity Framework models and migrations
  - `wwwroot/` - Static assets
- `BoardGameMondays.Tests/` - Test suite with migration validation