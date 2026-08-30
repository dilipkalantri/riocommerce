using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductEstimatedDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 📦 Estimated Delivery Information columns ──
            // Defaults mirror the C# entity initializers so existing rows look sensible until an
            // admin edits the product. EstimatedDeliveryMessage stays nullable (it's optional).
            migrationBuilder.AddColumn<string>(
                name: "EstimatedDeliveryMessage",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LectureAccessTiming",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "Within 24 Hours");

            migrationBuilder.AddColumn<string>(
                name: "NotesDispatchTimeline",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "Within 48 Hours");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedDeliveryMessage",
                table: "products");

            migrationBuilder.DropColumn(
                name: "LectureAccessTiming",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NotesDispatchTimeline",
                table: "products");
        }
    }
}
