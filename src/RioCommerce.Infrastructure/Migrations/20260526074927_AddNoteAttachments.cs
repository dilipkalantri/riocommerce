using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "note_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    HashChecksum = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    VirusScanStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedByName = table.Column<string>(type: "text", nullable: false),
                    UploadedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_note_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_note_attachments_order_notes_OrderNoteId",
                        column: x => x.OrderNoteId,
                        principalTable: "order_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_note_attachments_OrderId",
                table: "note_attachments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_note_attachments_OrderNoteId",
                table: "note_attachments",
                column: "OrderNoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "note_attachments");
        }
    }
}
