using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributeSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CheckoutAttributesAmount",
                table: "orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutAttributesJson",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedAttributesJson",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AttributePriceAdjustment",
                table: "CartItems",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SelectedAttributesJson",
                table: "CartItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckoutAttributesAmount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CheckoutAttributesJson",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SelectedAttributesJson",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AttributePriceAdjustment",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "SelectedAttributesJson",
                table: "CartItems");
        }
    }
}
