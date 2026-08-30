using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultyShowOnHomePage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowOnHomePage",
                table: "faculty",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111101"),
                column: "ShowOnHomePage",
                value: false);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111102"),
                column: "ShowOnHomePage",
                value: false);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111103"),
                column: "ShowOnHomePage",
                value: false);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111104"),
                column: "ShowOnHomePage",
                value: false);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111105"),
                column: "ShowOnHomePage",
                value: false);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111106"),
                column: "ShowOnHomePage",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowOnHomePage",
                table: "faculty");
        }
    }
}
