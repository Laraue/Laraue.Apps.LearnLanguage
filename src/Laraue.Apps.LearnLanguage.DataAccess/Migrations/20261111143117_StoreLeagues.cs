using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class StoreLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "league",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "learned_words",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "league",
                table: "users");

            migrationBuilder.DropColumn(
                name: "learned_words",
                table: "users");
        }
    }
}
