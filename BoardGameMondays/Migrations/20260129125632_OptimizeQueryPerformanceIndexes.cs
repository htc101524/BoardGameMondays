using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeQueryPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserPurchases_UserId",
                table: "UserPurchases",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_IsActive",
                table: "ShopItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGames_IsConfirmed",
                table: "GameNightGames",
                column: "IsConfirmed");

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGames_IsPlayed",
                table: "GameNightGames",
                column: "IsPlayed");

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGameBets_IsResolved",
                table: "GameNightGameBets",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGameBets_UserId",
                table: "GameNightGameBets",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPurchases_UserId",
                table: "UserPurchases");

            migrationBuilder.DropIndex(
                name: "IX_ShopItems_IsActive",
                table: "ShopItems");

            migrationBuilder.DropIndex(
                name: "IX_GameNightGames_IsConfirmed",
                table: "GameNightGames");

            migrationBuilder.DropIndex(
                name: "IX_GameNightGames_IsPlayed",
                table: "GameNightGames");

            migrationBuilder.DropIndex(
                name: "IX_GameNightGameBets_IsResolved",
                table: "GameNightGameBets");

            migrationBuilder.DropIndex(
                name: "IX_GameNightGameBets_UserId",
                table: "GameNightGameBets");
        }
    }
}
