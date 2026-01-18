using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGameMondays.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberEloRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns may already exist from the defensive schema upgrader in Program.cs.
            // Use raw SQL with IF NOT EXISTS checks to make this migration idempotent.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Members', N'EloRating') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [EloRating] int NOT NULL DEFAULT 1200;
END;
");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Members', N'EloRatingUpdatedOn') IS NULL
BEGIN
    ALTER TABLE [dbo].[Members] ADD [EloRatingUpdatedOn] bigint NULL;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EloRating",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "EloRatingUpdatedOn",
                table: "Members");
        }
    }
}
