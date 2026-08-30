using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    BatchNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BeneficiaryType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BeneficiaryCount = table.Column<int>(type: "integer", nullable: false),
                    TotalGross = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalAdjustments = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalRefundAdjustments = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalTax = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalNet = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByName = table.Column<string>(type: "text", nullable: true),
                    ApprovedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    BeneficiaryType = table.Column<int>(type: "integer", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficiaryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Adjustments = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RefundAdjustments = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TaxDeduction = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByName = table.Column<string>(type: "text", nullable: true),
                    ApprovedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payouts_settlement_batches_SettlementBatchId",
                        column: x => x.SettlementBatchId,
                        principalTable: "settlement_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "settlement_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_settlement_adjustments_settlement_batches_SettlementBatchId",
                        column: x => x.SettlementBatchId,
                        principalTable: "settlement_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payout_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    SharingRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payout_items_payouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "payouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_OrderItemId_Source",
                table: "payout_items",
                columns: new[] { "OrderItemId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_PayoutId",
                table: "payout_items",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_RefundId",
                table: "payout_items",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_BeneficiaryType_BeneficiaryId",
                table: "payouts",
                columns: new[] { "BeneficiaryType", "BeneficiaryId" });

            migrationBuilder.CreateIndex(
                name: "IX_payouts_SettlementBatchId",
                table: "payouts",
                column: "SettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_Status",
                table: "payouts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_adjustments_SettlementBatchId",
                table: "settlement_adjustments",
                column: "SettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_batches_BatchNumber",
                table: "settlement_batches",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_batches_Status",
                table: "settlement_batches",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_items");

            migrationBuilder.DropTable(
                name: "settlement_adjustments");

            migrationBuilder.DropTable(
                name: "payouts");

            migrationBuilder.DropTable(
                name: "settlement_batches");
        }
    }
}
