using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ChooseLeaderboardName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "leaderboard_name",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            // Users who showed their name keep showing the first name (LeaderboardNameMode.FirstName).
            migrationBuilder.Sql("UPDATE users SET leaderboard_name = 1 WHERE show_name_on_leaderboard");

            migrationBuilder.DropColumn(
                name: "show_name_on_leaderboard",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "show_name_on_leaderboard",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE users SET show_name_on_leaderboard = leaderboard_name <> 0");

            migrationBuilder.DropColumn(
                name: "leaderboard_name",
                table: "users");
        }
    }
}
