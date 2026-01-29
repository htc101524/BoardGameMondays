using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddMinWinsRequiredToShopItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Provider-agnostic column addition - EF will use appropriate type for each provider
            // SQLite: INTEGER, SQL Server: int
            migrationBuilder.AddColumn<int>(
                name: "MinWinsRequired",
                table: "ShopItems",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinWinsRequired",
                table: "ShopItems");
        }
    }
}
