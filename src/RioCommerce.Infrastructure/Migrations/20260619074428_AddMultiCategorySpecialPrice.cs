using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCategorySpecialPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookPreviewPages_BookPreviewId_PageNumber",
                table: "BookPreviewPages");

            migrationBuilder.AddColumn<decimal>(
                name: "SpecialPrice",
                table: "products",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SpecialPriceEndDateUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SpecialPriceStartDateUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_categories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_categories_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "special_price_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OldPrice = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    NewPrice = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    OldStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OldEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_special_price_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_special_price_audits_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookPreviewPages_BookPreviewId",
                table: "BookPreviewPages",
                column: "BookPreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_CategoryId",
                table: "product_categories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_ProductId_CategoryId",
                table: "product_categories",
                columns: new[] { "ProductId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_special_price_audits_ModifiedAt",
                table: "special_price_audits",
                column: "ModifiedAt");

            migrationBuilder.CreateIndex(
                name: "IX_special_price_audits_ProductId",
                table: "special_price_audits",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_categories");

            migrationBuilder.DropTable(
                name: "special_price_audits");

            migrationBuilder.DropIndex(
                name: "IX_BookPreviewPages_BookPreviewId",
                table: "BookPreviewPages");

            migrationBuilder.DropColumn(
                name: "SpecialPrice",
                table: "products");

            migrationBuilder.DropColumn(
                name: "SpecialPriceEndDateUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "SpecialPriceStartDateUtc",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "IX_BookPreviewPages_BookPreviewId_PageNumber",
                table: "BookPreviewPages",
                columns: new[] { "BookPreviewId", "PageNumber" },
                unique: true);
        }
    }
}
