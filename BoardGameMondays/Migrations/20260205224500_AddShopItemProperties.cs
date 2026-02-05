using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddShopItemProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinWinsRequired",
                table: "ShopItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CreatedOn",
                table: "ShopItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "2026-02-05T00:00:00+00:00");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinWinsRequired",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "ShopItems");
        }
    }
}
