using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddGameScoreTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add score tracking columns to Games table (conditional - only if not exists)
            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.Games', N'AreScoresCountable') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Games] ADD [AreScoresCountable] bit NOT NULL DEFAULT 0;
                END
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.Games', N'HighScore') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Games] ADD [HighScore] int NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.Games', N'HighScoreAchievedOn') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Games] ADD [HighScoreAchievedOn] bigint NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.Games', N'HighScoreMemberId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Games] ADD [HighScoreMemberId] uniqueidentifier NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.Games', N'HighScoreMemberName') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[Games] ADD [HighScoreMemberName] nvarchar(128) NULL;
                END
            ");

            // Add score tracking columns to GameNightGames table (conditional - only if not exists)
            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.GameNightGames', N'Score') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[GameNightGames] ADD [Score] int NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.GameNightGames', N'IsHighScore') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[GameNightGames] ADD [IsHighScore] bit NOT NULL DEFAULT 0;
                END
            ");

            // Add MinWinsRequired to ShopItems (conditional - only if not exists)
            migrationBuilder.Sql(@"
                IF COL_LENGTH(N'dbo.ShopItems', N'MinWinsRequired') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[ShopItems] ADD [MinWinsRequired] int NOT NULL DEFAULT 0;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinWinsRequired",
                table: "ShopItems");

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
        }
    }
}
