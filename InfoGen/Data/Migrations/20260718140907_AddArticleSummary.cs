using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoGen.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "SavedArticles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Summary",
                table: "SavedArticles");
        }
    }
}
