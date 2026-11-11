using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "achievement_messages_off",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "achievements_check_requested_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_achievements",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement = table.Column<byte>(type: "smallint", nullable: false),
                    level = table.Column<byte>(type: "smallint", nullable: false),
                    achieved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_achievements", x => new { x.user_id, x.achievement, x.level });
                    table.ForeignKey(
                        name: "fk_user_achievements_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_achievements_check_requested_at",
                table: "users",
                column: "achievements_check_requested_at",
                filter: "achievements_check_requested_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_achievements_achievement_level",
                table: "user_achievements",
                columns: new[] { "achievement", "level" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_achievements");

            migrationBuilder.DropIndex(
                name: "ix_users_achievements_check_requested_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "achievement_messages_off",
                table: "users");

            migrationBuilder.DropColumn(
                name: "achievements_check_requested_at",
                table: "users");
        }
    }
}
