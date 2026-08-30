using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseDetailEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookPreviewPdfUrl",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaqsJson",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LecturesVideoUrl",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedProductSlugs",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestimonialVideoUrls",
                table: "products",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookPreviewPdfUrl",
                table: "products");

            migrationBuilder.DropColumn(
                name: "FaqsJson",
                table: "products");

            migrationBuilder.DropColumn(
                name: "LecturesVideoUrl",
                table: "products");

            migrationBuilder.DropColumn(
                name: "RelatedProductSlugs",
                table: "products");

            migrationBuilder.DropColumn(
                name: "TestimonialVideoUrls",
                table: "products");
        }
    }
}
