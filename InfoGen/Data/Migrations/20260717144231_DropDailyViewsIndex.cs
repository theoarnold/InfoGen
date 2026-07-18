using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoGen.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropDailyViewsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedArticles_DailyViewsDate_DailyViews",
                table: "SavedArticles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SavedArticles_DailyViewsDate_DailyViews",
                table: "SavedArticles",
                columns: new[] { "DailyViewsDate", "DailyViews" });
        }
    }
}
