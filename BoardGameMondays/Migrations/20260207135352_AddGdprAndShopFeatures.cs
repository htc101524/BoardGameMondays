using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddGdprAndShopFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop index if it exists
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_GameNightGames_GameNightId_GameId' AND object_id = OBJECT_ID('GameNightGames'))
                DROP INDEX [IX_GameNightGames_GameNightId_GameId] ON [GameNightGames]
            ");

            // Add columns only if they don't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ShopItems') AND name = 'MinWinsRequired')
                ALTER TABLE [ShopItems] ADD [MinWinsRequired] int NOT NULL DEFAULT 0
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Members') AND name = 'LastMondayCoinsClaimedDateKey')
                ALTER TABLE [Members] ADD [LastMondayCoinsClaimedDateKey] int NULL
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Games') AND name = 'AreScoresCountable')
                ALTER TABLE [Games] ADD [AreScoresCountable] bit NOT NULL DEFAULT 0
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Games') AND name = 'HighScore')
                ALTER TABLE [Games] ADD [HighScore] int NULL
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Games') AND name = 'HighScoreAchievedOn')
                ALTER TABLE [Games] ADD [HighScoreAchievedOn] bigint NULL
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Games') AND name = 'HighScoreMemberId')
                ALTER TABLE [Games] ADD [HighScoreMemberId] uniqueidentifier NULL
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Games') AND name = 'HighScoreMemberName')
                ALTER TABLE [Games] ADD [HighScoreMemberName] nvarchar(128) NULL
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('GameNightGames') AND name = 'IsHighScore')
                ALTER TABLE [GameNightGames] ADD [IsHighScore] bit NOT NULL DEFAULT 0
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('GameNightGames') AND name = 'Score')
                ALTER TABLE [GameNightGames] ADD [Score] int NULL
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('BlogPosts') AND name = 'IsAdminOnly')
                ALTER TABLE [BlogPosts] ADD [IsAdminOnly] bit NOT NULL DEFAULT 0
            ");

            // Create tables only if they don't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DataDeletionRequests')
                BEGIN
                    CREATE TABLE [DataDeletionRequests] (
                        [Id] uniqueidentifier NOT NULL,
                        [UserId] nvarchar(450) NOT NULL,
                        [Email] nvarchar(256) NOT NULL,
                        [RequestedOn] bigint NOT NULL,
                        [ScheduledDeletionOn] bigint NOT NULL,
                        [CompletedOn] bigint NULL,
                        [Status] nvarchar(32) NOT NULL,
                        [Reason] nvarchar(1024) NULL,
                        [CancelledOn] bigint NULL,
                        CONSTRAINT [PK_DataDeletionRequests] PRIMARY KEY ([Id])
                    )
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserConsents')
                BEGIN
                    CREATE TABLE [UserConsents] (
                        [Id] uniqueidentifier NOT NULL,
                        [UserId] nvarchar(450) NULL,
                        [AnonymousId] nvarchar(128) NULL,
                        [ConsentType] nvarchar(64) NOT NULL,
                        [PolicyVersion] nvarchar(32) NOT NULL,
                        [IsGranted] bit NOT NULL,
                        [ConsentedOn] bigint NOT NULL,
                        [IpAddress] nvarchar(45) NULL,
                        [UserAgent] nvarchar(512) NULL,
                        CONSTRAINT [PK_UserConsents] PRIMARY KEY ([Id])
                    )
                END
            ");

            // Create indexes only if they don't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_GameNightGames_GameNightId_GameId' AND object_id = OBJECT_ID('GameNightGames'))
                CREATE INDEX [IX_GameNightGames_GameNightId_GameId] ON [GameNightGames] ([GameNightId], [GameId])
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DataDeletionRequests_Status' AND object_id = OBJECT_ID('DataDeletionRequests'))
                CREATE INDEX [IX_DataDeletionRequests_Status] ON [DataDeletionRequests] ([Status])
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DataDeletionRequests_UserId' AND object_id = OBJECT_ID('DataDeletionRequests'))
                CREATE INDEX [IX_DataDeletionRequests_UserId] ON [DataDeletionRequests] ([UserId])
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserConsents_AnonymousId' AND object_id = OBJECT_ID('UserConsents'))
                CREATE INDEX [IX_UserConsents_AnonymousId] ON [UserConsents] ([AnonymousId])
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserConsents_UserId' AND object_id = OBJECT_ID('UserConsents'))
                CREATE INDEX [IX_UserConsents_UserId] ON [UserConsents] ([UserId])
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserConsents_UserId_ConsentType' AND object_id = OBJECT_ID('UserConsents'))
                CREATE INDEX [IX_UserConsents_UserId_ConsentType] ON [UserConsents] ([UserId], [ConsentType])
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataDeletionRequests");

            migrationBuilder.DropTable(
                name: "UserConsents");

            migrationBuilder.DropIndex(
                name: "IX_GameNightGames_GameNightId_GameId",
                table: "GameNightGames");

            migrationBuilder.DropColumn(
                name: "MinWinsRequired",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "LastMondayCoinsClaimedDateKey",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "AreScoresCountable",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScore",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScoreAchievedOn",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScoreMemberId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScoreMemberName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "IsHighScore",
                table: "GameNightGames");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "GameNightGames");

            migrationBuilder.DropColumn(
                name: "IsAdminOnly",
                table: "BlogPosts");

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGames_GameNightId_GameId",
                table: "GameNightGames",
                columns: new[] { "GameNightId", "GameId" },
                unique: true);
        }
    }
}
