using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTrophies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The weekly league job is replaced by the daily season job.
            migrationBuilder.Sql("DELETE FROM job_states WHERE job_name = 'UpdateLeagues'");

            migrationBuilder.CreateTable(
                name: "user_trophies",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season = table.Column<byte>(type: "smallint", nullable: false),
                    season_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    league = table.Column<byte>(type: "smallint", nullable: false),
                    place = table.Column<byte>(type: "smallint", nullable: false),
                    awarded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_trophies", x => new { x.user_id, x.season, x.season_start });
                    table.ForeignKey(
                        name: "fk_user_trophies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_trophies");
        }
    }
}
