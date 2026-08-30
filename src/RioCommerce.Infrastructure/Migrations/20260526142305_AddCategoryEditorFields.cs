using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryEditorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeInTopMenu",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SeoDescription",
                table: "Categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoKeywords",
                table: "Categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoTitle",
                table: "Categories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowOnHomePage",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222201"),
                columns: new[] { "IncludeInTopMenu", "SeoDescription", "SeoKeywords", "SeoTitle", "ShowOnHomePage" },
                values: new object[] { false, null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222202"),
                columns: new[] { "IncludeInTopMenu", "SeoDescription", "SeoKeywords", "SeoTitle", "ShowOnHomePage" },
                values: new object[] { false, null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222203"),
                columns: new[] { "IncludeInTopMenu", "SeoDescription", "SeoKeywords", "SeoTitle", "ShowOnHomePage" },
                values: new object[] { false, null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222204"),
                columns: new[] { "IncludeInTopMenu", "SeoDescription", "SeoKeywords", "SeoTitle", "ShowOnHomePage" },
                values: new object[] { false, null, null, null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeInTopMenu",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SeoDescription",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SeoKeywords",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SeoTitle",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ShowOnHomePage",
                table: "Categories");
        }
    }
}
