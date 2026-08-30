using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSerialKeyIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookPreviewPages_BookPreviewId",
                table: "BookPreviewPages");

            migrationBuilder.CreateTable(
                name: "product_serial_key_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConfigJson = table.Column<string>(type: "jsonb", nullable: false),
                    AutoActivate = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_serial_key_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_serial_key_configs_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rioplay_tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    SecretEncrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rioplay_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "serial_key_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TenantRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SerialKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestPayload = table.Column<string>(type: "jsonb", nullable: false),
                    ResponsePayload = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serial_key_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_serial_key_records_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_key_records_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_key_records_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_key_records_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookPreviewPages_BookPreviewId_PageNumber",
                table: "BookPreviewPages",
                columns: new[] { "BookPreviewId", "PageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_serial_key_configs_ProductId",
                table: "product_serial_key_configs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_serial_key_configs_ProductId_ProviderKey_IsActive",
                table: "product_serial_key_configs",
                columns: new[] { "ProductId", "ProviderKey", "IsActive" },
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_rioplay_tenants_IsDefault",
                table: "rioplay_tenants",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_rioplay_tenants_TenantId",
                table: "rioplay_tenants",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serial_key_records_OrderId",
                table: "serial_key_records",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_key_records_OrderItemId",
                table: "serial_key_records",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_key_records_ProductId",
                table: "serial_key_records",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_key_records_SerialKey",
                table: "serial_key_records",
                column: "SerialKey",
                unique: true,
                filter: "\"SerialKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_serial_key_records_Status_NextRetryAt",
                table: "serial_key_records",
                columns: new[] { "Status", "NextRetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_serial_key_records_UserId",
                table: "serial_key_records",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_serial_key_configs");

            migrationBuilder.DropTable(
                name: "rioplay_tenants");

            migrationBuilder.DropTable(
                name: "serial_key_records");

            migrationBuilder.DropIndex(
                name: "IX_BookPreviewPages_BookPreviewId_PageNumber",
                table: "BookPreviewPages");

            migrationBuilder.CreateIndex(
                name: "IX_BookPreviewPages_BookPreviewId",
                table: "BookPreviewPages",
                column: "BookPreviewId");
        }
    }
}
