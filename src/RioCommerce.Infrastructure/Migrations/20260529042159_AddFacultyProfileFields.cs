using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultyProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AirHoldersNote",
                table: "faculty",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallNumber",
                table: "faculty",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoursOfTeaching",
                table: "faculty",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudentSatisfaction",
                table: "faculty",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudentsTaught",
                table: "faculty",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YearsOfExperience",
                table: "faculty",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111101"),
                columns: new[] { "AirHoldersNote", "CallNumber", "HoursOfTeaching", "StudentSatisfaction", "StudentsTaught", "YearsOfExperience" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111102"),
                columns: new[] { "AirHoldersNote", "CallNumber", "HoursOfTeaching", "StudentSatisfaction", "StudentsTaught", "YearsOfExperience" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111103"),
                columns: new[] { "AirHoldersNote", "CallNumber", "HoursOfTeaching", "StudentSatisfaction", "StudentsTaught", "YearsOfExperience" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111104"),
                columns: new[] { "AirHoldersNote", "CallNumber", "HoursOfTeaching", "StudentSatisfaction", "StudentsTaught", "YearsOfExperience" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111105"),
                columns: new[] { "AirHoldersNote", "CallNumber", "HoursOfTeaching", "StudentSatisfaction", "StudentsTaught", "YearsOfExperience" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "faculty",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111106"),
                columns: new[] { "AirHoldersNote", "CallNumber", "HoursOfTeaching", "StudentSatisfaction", "StudentsTaught", "YearsOfExperience" },
                values: new object[] { null, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AirHoldersNote",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "CallNumber",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "HoursOfTeaching",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "StudentSatisfaction",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "StudentsTaught",
                table: "faculty");

            migrationBuilder.DropColumn(
                name: "YearsOfExperience",
                table: "faculty");
        }
    }
}
