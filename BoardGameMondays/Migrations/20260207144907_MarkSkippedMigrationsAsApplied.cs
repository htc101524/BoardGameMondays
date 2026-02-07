using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class MarkSkippedMigrationsAsApplied : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mark all the old SQLite migrations as applied without running them
            // This prevents EF from trying to apply these invalid migrations
            
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = '20260205103723_AddGdprConsent')
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260205103723_AddGdprConsent', '9.0.1')
            ");
            
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = '20260205150000_AddMondayAttendanceCoins')
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260205150000_AddMondayAttendanceCoins', '9.0.1')
            ");
            
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = '20260205214935_CapturePendingModelChanges')
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260205214935_CapturePendingModelChanges', '9.0.1')
            ");
            
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = '20260205220911_CheckPendingChanges')
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260205220911_CheckPendingChanges', '9.0.1')
            ");
            
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = '20260205224500_AddShopItemProperties')
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260205224500_AddShopItemProperties', '9.0.1')
            ");
            
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE MigrationId = '20260206122954_SyncModelSnapshot')
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20260206122954_SyncModelSnapshot', '9.0.1')
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the marked migrations on rollback
            migrationBuilder.Sql("DELETE FROM [__EFMigrationsHistory] WHERE MigrationId IN ('20260205103723_AddGdprConsent', '20260205150000_AddMondayAttendanceCoins', '20260205214935_CapturePendingModelChanges', '20260205220911_CheckPendingChanges', '20260205224500_AddShopItemProperties', '20260206122954_SyncModelSnapshot')");
        }
    }
}
