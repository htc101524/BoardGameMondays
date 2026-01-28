using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddGameStartedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasStarted",
                table: "GameNights",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasStarted",
                table: "GameNights");
        }
    }
}
