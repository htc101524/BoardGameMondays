using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class PendingChanges_20260115 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FavoriteGame",
                table: "Members",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FunFact",
                table: "Members",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayStyle",
                table: "Members",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileTagline",
                table: "Members",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnackBrought",
                table: "GameNightAttendees",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GameNightRsvps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsAttending = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNightRsvps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameNightRsvps_GameNights_GameNightId",
                        column: x => x.GameNightId,
                        principalTable: "GameNights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameNightRsvps_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VictoryRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VictoryRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VictoryRoutes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WantToPlayVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WeekKey = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WantToPlayVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WantToPlayVotes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameNightGameVictoryRouteValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GameNightGameId = table.Column<int>(type: "int", nullable: false),
                    VictoryRouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValueString = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ValueBool = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameNightGameVictoryRouteValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameNightGameVictoryRouteValues_GameNightGames_GameNightGameId",
                        column: x => x.GameNightGameId,
                        principalTable: "GameNightGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameNightGameVictoryRouteValues_VictoryRoutes_VictoryRouteId",
                        column: x => x.VictoryRouteId,
                        principalTable: "VictoryRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VictoryRouteOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VictoryRouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VictoryRouteOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VictoryRouteOptions_VictoryRoutes_VictoryRouteId",
                        column: x => x.VictoryRouteId,
                        principalTable: "VictoryRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGameVictoryRouteValues_GameNightGameId_VictoryRouteId",
                table: "GameNightGameVictoryRouteValues",
                columns: new[] { "GameNightGameId", "VictoryRouteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameNightGameVictoryRouteValues_VictoryRouteId",
                table: "GameNightGameVictoryRouteValues",
                column: "VictoryRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_GameNightRsvps_GameNightId_MemberId",
                table: "GameNightRsvps",
                columns: new[] { "GameNightId", "MemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameNightRsvps_MemberId",
                table: "GameNightRsvps",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_VictoryRouteOptions_VictoryRouteId_SortOrder",
                table: "VictoryRouteOptions",
                columns: new[] { "VictoryRouteId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VictoryRoutes_GameId_SortOrder",
                table: "VictoryRoutes",
                columns: new[] { "GameId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WantToPlayVotes_GameId",
                table: "WantToPlayVotes",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_WantToPlayVotes_UserId_GameId_WeekKey",
                table: "WantToPlayVotes",
                columns: new[] { "UserId", "GameId", "WeekKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WantToPlayVotes_UserId_WeekKey",
                table: "WantToPlayVotes",
                columns: new[] { "UserId", "WeekKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameNightGameVictoryRouteValues");

            migrationBuilder.DropTable(
                name: "GameNightRsvps");

            migrationBuilder.DropTable(
                name: "VictoryRouteOptions");

            migrationBuilder.DropTable(
                name: "WantToPlayVotes");

            migrationBuilder.DropTable(
                name: "VictoryRoutes");

            migrationBuilder.DropColumn(
                name: "FavoriteGame",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "FunFact",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "PlayStyle",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ProfileTagline",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "SnackBrought",
                table: "GameNightAttendees");
        }
    }
}
