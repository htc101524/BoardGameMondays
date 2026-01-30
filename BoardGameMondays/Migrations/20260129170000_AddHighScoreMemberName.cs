using Microsoft.EntityFrameworkCore.Migrations;

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddHighScoreMemberName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HighScoreMemberName",
                table: "Games",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HighScoreMemberName",
                table: "Games");
        }
    }
}
