using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievementsListRequested : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "achievements_list_requested",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "achievements_list_requested",
                table: "users");
        }
    }
}
