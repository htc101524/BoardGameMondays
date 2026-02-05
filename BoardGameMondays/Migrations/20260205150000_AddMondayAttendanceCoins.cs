using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    [Migration("20260205150000_AddMondayAttendanceCoins")]
    public partial class AddMondayAttendanceCoins : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastMondayCoinsClaimedDateKey",
                table: "Members",
                type: "INTEGER",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastMondayCoinsClaimedDateKey",
                table: "Members");
        }
    }
}
