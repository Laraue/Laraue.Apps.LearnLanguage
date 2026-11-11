using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievementTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "title_achievement",
                table: "users",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "title_level",
                table: "users",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "title_achievement",
                table: "users");

            migrationBuilder.DropColumn(
                name: "title_level",
                table: "users");
        }
    }
}
