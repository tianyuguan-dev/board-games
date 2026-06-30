using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BoardGames.Migrations
{
    /// <inheritdoc />
    public partial class AddIsRanked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRanked",
                table: "AvalonGameHistories",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRanked",
                table: "AvalonGameHistories");
        }
    }
}
