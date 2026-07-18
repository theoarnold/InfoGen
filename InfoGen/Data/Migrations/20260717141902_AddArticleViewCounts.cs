using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoGen.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleViewCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyViews",
                table: "SavedArticles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DailyViewsDate",
                table: "SavedArticles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalViews",
                table: "SavedArticles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SavedArticles_DailyViewsDate_DailyViews",
                table: "SavedArticles",
                columns: new[] { "DailyViewsDate", "DailyViews" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedArticles_DailyViewsDate_DailyViews",
                table: "SavedArticles");

            migrationBuilder.DropColumn(
                name: "DailyViews",
                table: "SavedArticles");

            migrationBuilder.DropColumn(
                name: "DailyViewsDate",
                table: "SavedArticles");

            migrationBuilder.DropColumn(
                name: "TotalViews",
                table: "SavedArticles");
        }
    }
}
