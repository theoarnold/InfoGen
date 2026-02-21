using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoGen.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "SavedArticles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SavedArticles_Slug",
                table: "SavedArticles",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedArticles_Slug",
                table: "SavedArticles");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "SavedArticles");
        }
    }
}
