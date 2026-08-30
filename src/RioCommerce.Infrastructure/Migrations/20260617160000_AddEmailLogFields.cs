using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailLogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHtml",
                table: "notification_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "notification_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Response",
                table: "notification_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "notification_logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggeredBy",
                table: "notification_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestPayload",
                table: "notification_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "notification_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalLogId",
                table: "notification_logs",
                type: "uuid",
                nullable: true);

            // Indexes used by the Email Logs grid filters.
            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_Status",
                table: "notification_logs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_Provider",
                table: "notification_logs",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_Recipient",
                table: "notification_logs",
                column: "Recipient");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_notification_logs_Recipient", table: "notification_logs");
            migrationBuilder.DropIndex(name: "IX_notification_logs_Provider",  table: "notification_logs");
            migrationBuilder.DropIndex(name: "IX_notification_logs_Status",    table: "notification_logs");
            migrationBuilder.DropColumn(name: "OriginalLogId",                 table: "notification_logs");
            migrationBuilder.DropColumn(name: "RetryCount",                    table: "notification_logs");
            migrationBuilder.DropColumn(name: "RequestPayload",                table: "notification_logs");
            migrationBuilder.DropColumn(name: "TriggeredBy",                   table: "notification_logs");
            migrationBuilder.DropColumn(name: "DurationMs",                    table: "notification_logs");
            migrationBuilder.DropColumn(name: "Response",                      table: "notification_logs");
            migrationBuilder.DropColumn(name: "Provider",                      table: "notification_logs");
            migrationBuilder.DropColumn(name: "IsHtml",                        table: "notification_logs");
        }
    }
}
