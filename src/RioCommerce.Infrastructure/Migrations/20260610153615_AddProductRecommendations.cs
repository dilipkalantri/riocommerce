using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfferImageUrl",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_recommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CustomTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    BadgeText = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BadgeColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_recommendations_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_recommendations_products_RecommendedProductId",
                        column: x => x.RecommendedProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_recommendations_ProductId_IsActive_Priority_Display~",
                table: "product_recommendations",
                columns: new[] { "ProductId", "IsActive", "Priority", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_product_recommendations_ProductId_RecommendedProductId",
                table: "product_recommendations",
                columns: new[] { "ProductId", "RecommendedProductId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_product_recommendations_RecommendedProductId",
                table: "product_recommendations",
                column: "RecommendedProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_recommendations");

            migrationBuilder.DropColumn(
                name: "OfferImageUrl",
                table: "products");
        }
    }
}
