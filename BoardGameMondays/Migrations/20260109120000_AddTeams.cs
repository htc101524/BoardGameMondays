using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    public partial class AddTeams : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WinnerTeamName",
                table: "GameNightGames",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamName",
                table: "GameNightGamePlayers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WinnerTeamName",
                table: "GameNightGames");

            migrationBuilder.DropColumn(
                name: "TeamName",
                table: "GameNightGamePlayers");
        }
    }
}
