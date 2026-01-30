using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    [DbContext(typeof(BoardGameMondays.Data.ApplicationDbContext))]
    [Migration("20260129160000_AddGameScoreTracking")]
    public partial class AddGameScoreTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AreScoresCountable",
                table: "Games",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HighScore",
                table: "Games",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HighScoreMemberId",
                table: "Games",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HighScoreAchievedOn",
                table: "Games",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "GameNightGames",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHighScore",
                table: "GameNightGames",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreScoresCountable",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScore",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScoreMemberId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HighScoreAchievedOn",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "GameNightGames");

            migrationBuilder.DropColumn(
                name: "IsHighScore",
                table: "GameNightGames");
        }
    }
}
