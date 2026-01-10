using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamColours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameNightGameTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameNightGameId = table.Column<int>(type: "int", nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ColorHex = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNightGameTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameNightGameTeams_GameNightGames_GameNightGameId",
                        column: x => x.GameNightGameId,
                        principalTable: "GameNightGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGameTeams_GameNightGameId_TeamName",
                table: "GameNightGameTeams",
                columns: new[] { "GameNightGameId", "TeamName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameNightGameTeams");
        }
    }
}
