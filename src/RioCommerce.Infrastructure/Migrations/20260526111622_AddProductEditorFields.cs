using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductEditorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminComment",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowReviews",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableEndUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableStartUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gtin",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MarkAsNew",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ProductCost",
                table: "products",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ProductImages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminComment",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AllowReviews",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AvailableEndUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AvailableStartUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Gtin",
                table: "products");

            migrationBuilder.DropColumn(
                name: "MarkAsNew",
                table: "products");

            migrationBuilder.DropColumn(
                name: "ProductCost",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ProductImages");
        }
    }
}
