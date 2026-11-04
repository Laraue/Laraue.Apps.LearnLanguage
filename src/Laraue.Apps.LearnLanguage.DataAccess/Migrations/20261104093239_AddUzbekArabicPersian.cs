using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Laraue.Apps.LearnLanguage.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddUzbekArabicPersian : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "languages",
                columns: new[] { "id", "description", "name" },
                values: new object[,]
                {
                    { 9L, "Uzbek", "uz" },
                    { 10L, "Arabic", "ar" },
                    { 11L, "Persian", "fa" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "languages",
                keyColumn: "id",
                keyValue: 9L);

            migrationBuilder.DeleteData(
                table: "languages",
                keyColumn: "id",
                keyValue: 10L);

            migrationBuilder.DeleteData(
                table: "languages",
                keyColumn: "id",
                keyValue: 11L);
        }
    }
}
