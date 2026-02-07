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
            migrationBuilder.DropIndex(
                name: "IX_GameNightGames_GameNightId_GameId",
                table: "GameNightGames");

            migrationBuilder.AddColumn<int>(
                name: "MinWinsRequired",
                table: "ShopItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastMondayCoinsClaimedDateKey",
                table: "Members",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AreScoresCountable",
                table: "Games",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HighScore",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HighScoreAchievedOn",
                table: "Games",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HighScoreMemberId",
                table: "Games",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HighScoreMemberName",
                table: "Games",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHighScore",
                table: "GameNightGames",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "GameNightGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdminOnly",
                table: "BlogPosts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DataDeletionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestedOn = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledDeletionOn = table.Column<long>(type: "bigint", nullable: false),
                    CompletedOn = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CancelledOn = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataDeletionRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AnonymousId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ConsentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false),
                    ConsentedOn = table.Column<long>(type: "bigint", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConsents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGames_GameNightId_GameId",
                table: "GameNightGames",
                columns: new[] { "GameNightId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_DataDeletionRequests_Status",
                table: "DataDeletionRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DataDeletionRequests_UserId",
                table: "DataDeletionRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_AnonymousId",
                table: "UserConsents",
                column: "AnonymousId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_UserId",
                table: "UserConsents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserConsents_UserId_ConsentType",
                table: "UserConsents",
                columns: new[] { "UserId", "ConsentType" });
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
