using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddShopAndReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameResultReactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameNightGameId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameResultReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameResultReactions_GameNightGames_GameNightGameId",
                        column: x => x.GameNightGameId,
                        principalTable: "GameNightGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Price = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MembersOnly = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ShopItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchasedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPurchases_ShopItems_ShopItemId",
                        column: x => x.ShopItemId,
                        principalTable: "ShopItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameResultReactions_GameNightGameId",
                table: "GameResultReactions",
                column: "GameNightGameId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPurchases_ShopItemId",
                table: "UserPurchases",
                column: "ShopItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameResultReactions");

            migrationBuilder.DropTable(
                name: "UserPurchases");

            migrationBuilder.DropTable(
                name: "ShopItems");
        }
    }
}
