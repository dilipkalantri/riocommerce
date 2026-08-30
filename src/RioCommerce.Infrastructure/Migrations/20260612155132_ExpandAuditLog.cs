using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "audit_logs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "audit_logs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "audit_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Device",
                table: "audit_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityName",
                table: "audit_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "audit_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "audit_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewValue",
                table: "audit_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldValue",
                table: "audit_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "audit_logs",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Action",
                table: "audit_logs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ActorUserId",
                table: "audit_logs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Module",
                table: "audit_logs",
                column: "Module");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_Action",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_ActorUserId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_Module",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "Browser",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "Device",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "EntityName",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "NewValue",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "OldValue",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "audit_logs");

            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "audit_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "audit_logs",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);
        }
    }
}
