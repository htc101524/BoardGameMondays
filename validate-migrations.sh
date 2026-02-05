#!/bin/bash
# Migration Validation Script
# Run this before deploying to catch migration issues early
# Usage: ./validate-migrations.sh

set -e

echo "=========================================="
echo "Migration Validation Script"
echo "=========================================="
echo ""

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT/BoardGameMondays"

echo "✓ Checking if dotnet is installed..."
if ! command -v dotnet &> /dev/null; then
    echo "✗ dotnet CLI not found. Please install .NET SDK."
    exit 1
fi

echo "✓ Building project..."
if ! dotnet build --configuration Release --verbosity quiet; then
    echo "✗ Build failed!"
    exit 1
fi

echo "✓ Getting list of migrations..."
MIGRATION_COUNT=$(dotnet ef migrations list --no-build 2>/dev/null | wc -l)
echo "  Found $MIGRATION_COUNT migrations"

echo ""
echo "✓ Checking for pending migrations in design-time context..."
# This will fail if there are unmigrated model changes
if dotnet ef migrations add _ValidationCheck --dry-run --no-build 2>&1 | grep -q "A migration named '_ValidationCheck' already exists"; then
    echo "  No pending changes detected ✓"
elif dotnet ef migrations add _ValidationCheck --dry-run --no-build 2>&1 | grep -q "No changes detected"; then
    echo "  No pending changes detected ✓"
else
    echo "✗ PENDING MODEL CHANGES DETECTED!"
    echo "  Run: dotnet ef migrations add [MigrationName]"
    exit 1
fi

echo ""
echo "✓ Running migration tests..."
if ! dotnet test ../BoardGameMondays.Tests/BoardGameMondays.Tests.csproj \
    --filter "FullyQualifiedName~MigrationTests" \
    --configuration Release \
    --verbosity quiet \
    --no-build 2>/dev/null; then
    echo "✗ Migration tests failed!"
    exit 1
fi

echo ""
echo "=========================================="
echo "✓ All migration validations passed!"
echo "=========================================="
echo ""
echo "Safe to deploy!"
