using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonsRemoveLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The weekly league job is replaced by the daily season job.
            migrationBuilder.Sql("DELETE FROM job_states WHERE job_name = 'UpdateLeagues'");

            migrationBuilder.DropColumn(
                name: "league",
                table: "users");

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<byte>(type: "smallint", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_trophies",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<long>(type: "bigint", nullable: false),
                    place = table.Column<byte>(type: "smallint", nullable: false),
                    awarded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_trophies", x => new { x.user_id, x.season_id });
                    table.ForeignKey(
                        name: "fk_user_trophies_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_trophies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_seasons_type_starts_at",
                table: "seasons",
                columns: new[] { "type", "starts_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_trophies_season_id",
                table: "user_trophies",
                column: "season_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_trophies");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.AddColumn<byte>(
                name: "league",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);
        }
    }
}
