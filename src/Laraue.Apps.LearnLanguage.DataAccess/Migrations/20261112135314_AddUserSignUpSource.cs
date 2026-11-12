using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSignUpSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sign_up_source",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Users from before MIN-47 keep the source the funnel showed for them: the "source"/"utm_source" UTM
            // label, otherwise the first label as name-value. The same rule as the funnel query had, so the
            // dashboard's history doesn't change.
            migrationBuilder.Sql("""
                UPDATE users u
                SET sign_up_source = left(COALESCE(
                    (SELECT l.value FROM utm_labels l
                     WHERE l.user_id = u.id AND l.name IN ('source', 'utm_source') AND l.value <> ''
                     ORDER BY l.name LIMIT 1),
                    (SELECT l.name || CASE WHEN l.value <> '' THEN '-' || l.value ELSE '' END FROM utm_labels l
                     WHERE l.user_id = u.id
                     ORDER BY l.name LIMIT 1)), 64)
                WHERE u.sign_up_source IS NULL
                  AND EXISTS (SELECT 1 FROM utm_labels l WHERE l.user_id = u.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sign_up_source",
                table: "users");
        }
    }
}
