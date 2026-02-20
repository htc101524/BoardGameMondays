using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewPromptSentEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewPromptSents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentOn = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewPromptSents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewPromptSents_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewPromptSents_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewPromptSents_GameId",
                table: "ReviewPromptSents",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewPromptSents_MemberId",
                table: "ReviewPromptSents",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewPromptSents");
        }
    }
}
