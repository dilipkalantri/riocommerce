using System;
using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:attribute_control_type", "dropdown_list,radio_list,checkboxes,text_box,multiline_text_box,datepicker")
                .Annotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .Annotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .Annotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .Annotation("Npgsql:Enum:lead_status", "new,contacted,follow_up,converted,lost")
                .Annotation("Npgsql:Enum:lecture_mode", "live_streaming,recorded,live_plus_recorded,pendrive,face_to_face")
                .Annotation("Npgsql:Enum:order_source", "website,counter,franchisee,other")
                .Annotation("Npgsql:Enum:order_status", "draft,pending,confirmed,processing,activated,delivered,cancelled,refunded")
                .Annotation("Npgsql:Enum:payment_mode", "razorpay,easebuzz,ccavenue,upi,bank_transfer,cash,cheque,razorpay_link,emi")
                .Annotation("Npgsql:Enum:payment_status", "pending,success,failed,refunded,partial_refund")
                .Annotation("Npgsql:Enum:product_status", "active,draft,archived")
                .Annotation("Npgsql:Enum:review_status", "pending,approved,rejected")
                .Annotation("Npgsql:Enum:sharing_type", "percentage,fixed_amount")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .OldAnnotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .OldAnnotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .OldAnnotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .OldAnnotation("Npgsql:Enum:lead_status", "new,contacted,follow_up,converted,lost")
                .OldAnnotation("Npgsql:Enum:lecture_mode", "live_streaming,recorded,live_plus_recorded,pendrive,face_to_face")
                .OldAnnotation("Npgsql:Enum:order_source", "website,counter,franchisee,other")
                .OldAnnotation("Npgsql:Enum:order_status", "draft,pending,confirmed,processing,activated,delivered,cancelled,refunded")
                .OldAnnotation("Npgsql:Enum:payment_mode", "razorpay,easebuzz,ccavenue,upi,bank_transfer,cash,cheque,razorpay_link,emi")
                .OldAnnotation("Npgsql:Enum:payment_status", "pending,success,failed,refunded,partial_refund")
                .OldAnnotation("Npgsql:Enum:product_status", "active,draft,archived")
                .OldAnnotation("Npgsql:Enum:review_status", "pending,approved,rejected")
                .OldAnnotation("Npgsql:Enum:sharing_type", "percentage,fixed_amount")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "checkout_attributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    TextPrompt = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ControlType = table.Column<AttributeControlType>(type: "attribute_control_type", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkout_attributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_attributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_attributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "specification_attribute_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specification_attribute_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "checkout_attribute_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CheckoutAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    PriceAdjustment = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PriceAdjustmentUsePercentage = table.Column<bool>(type: "boolean", nullable: false),
                    IsPreSelected = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkout_attribute_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_checkout_attribute_values_checkout_attributes_CheckoutAttri~",
                        column: x => x.CheckoutAttributeId,
                        principalTable: "checkout_attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "predefined_product_attribute_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    PriceAdjustment = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PriceAdjustmentUsePercentage = table.Column<bool>(type: "boolean", nullable: false),
                    IsPreSelected = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_predefined_product_attribute_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_predefined_product_attribute_values_product_attributes_Prod~",
                        column: x => x.ProductAttributeId,
                        principalTable: "product_attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_attribute_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextPrompt = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ControlType = table.Column<AttributeControlType>(type: "attribute_control_type", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_attribute_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_attribute_mappings_product_attributes_ProductAttrib~",
                        column: x => x.ProductAttributeId,
                        principalTable: "product_attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_product_attribute_mappings_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "specification_attributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    SpecificationAttributeGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specification_attributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_specification_attributes_specification_attribute_groups_Spe~",
                        column: x => x.SpecificationAttributeGroupId,
                        principalTable: "specification_attribute_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "product_attribute_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductAttributeMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    PriceAdjustment = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PriceAdjustmentUsePercentage = table.Column<bool>(type: "boolean", nullable: false),
                    IsPreSelected = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_attribute_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_attribute_values_product_attribute_mappings_Product~",
                        column: x => x.ProductAttributeMappingId,
                        principalTable: "product_attribute_mappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "specification_attribute_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SpecificationAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ColorSquaresRgb = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specification_attribute_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_specification_attribute_options_specification_attributes_Sp~",
                        column: x => x.SpecificationAttributeId,
                        principalTable: "specification_attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_specification_attributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecificationAttributeOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowFiltering = table.Column<bool>(type: "boolean", nullable: false),
                    ShowOnProductPage = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_specification_attributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_specification_attributes_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_specification_attributes_specification_attribute_op~",
                        column: x => x.SpecificationAttributeOptionId,
                        principalTable: "specification_attribute_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_checkout_attribute_values_CheckoutAttributeId",
                table: "checkout_attribute_values",
                column: "CheckoutAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_predefined_product_attribute_values_ProductAttributeId",
                table: "predefined_product_attribute_values",
                column: "ProductAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_product_attribute_mappings_ProductAttributeId",
                table: "product_attribute_mappings",
                column: "ProductAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_product_attribute_mappings_ProductId",
                table: "product_attribute_mappings",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_attribute_values_ProductAttributeMappingId",
                table: "product_attribute_values",
                column: "ProductAttributeMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_product_specification_attributes_ProductId",
                table: "product_specification_attributes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_product_specification_attributes_SpecificationAttributeOpti~",
                table: "product_specification_attributes",
                column: "SpecificationAttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_specification_attribute_options_SpecificationAttributeId",
                table: "specification_attribute_options",
                column: "SpecificationAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_specification_attributes_SpecificationAttributeGroupId",
                table: "specification_attributes",
                column: "SpecificationAttributeGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkout_attribute_values");

            migrationBuilder.DropTable(
                name: "predefined_product_attribute_values");

            migrationBuilder.DropTable(
                name: "product_attribute_values");

            migrationBuilder.DropTable(
                name: "product_specification_attributes");

            migrationBuilder.DropTable(
                name: "checkout_attributes");

            migrationBuilder.DropTable(
                name: "product_attribute_mappings");

            migrationBuilder.DropTable(
                name: "specification_attribute_options");

            migrationBuilder.DropTable(
                name: "product_attributes");

            migrationBuilder.DropTable(
                name: "specification_attributes");

            migrationBuilder.DropTable(
                name: "specification_attribute_groups");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .Annotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .Annotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .Annotation("Npgsql:Enum:lead_status", "new,contacted,follow_up,converted,lost")
                .Annotation("Npgsql:Enum:lecture_mode", "live_streaming,recorded,live_plus_recorded,pendrive,face_to_face")
                .Annotation("Npgsql:Enum:order_source", "website,counter,franchisee,other")
                .Annotation("Npgsql:Enum:order_status", "draft,pending,confirmed,processing,activated,delivered,cancelled,refunded")
                .Annotation("Npgsql:Enum:payment_mode", "razorpay,easebuzz,ccavenue,upi,bank_transfer,cash,cheque,razorpay_link,emi")
                .Annotation("Npgsql:Enum:payment_status", "pending,success,failed,refunded,partial_refund")
                .Annotation("Npgsql:Enum:product_status", "active,draft,archived")
                .Annotation("Npgsql:Enum:review_status", "pending,approved,rejected")
                .Annotation("Npgsql:Enum:sharing_type", "percentage,fixed_amount")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .OldAnnotation("Npgsql:Enum:attribute_control_type", "dropdown_list,radio_list,checkboxes,text_box,multiline_text_box,datepicker")
                .OldAnnotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .OldAnnotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .OldAnnotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .OldAnnotation("Npgsql:Enum:lead_status", "new,contacted,follow_up,converted,lost")
                .OldAnnotation("Npgsql:Enum:lecture_mode", "live_streaming,recorded,live_plus_recorded,pendrive,face_to_face")
                .OldAnnotation("Npgsql:Enum:order_source", "website,counter,franchisee,other")
                .OldAnnotation("Npgsql:Enum:order_status", "draft,pending,confirmed,processing,activated,delivered,cancelled,refunded")
                .OldAnnotation("Npgsql:Enum:payment_mode", "razorpay,easebuzz,ccavenue,upi,bank_transfer,cash,cheque,razorpay_link,emi")
                .OldAnnotation("Npgsql:Enum:payment_status", "pending,success,failed,refunded,partial_refund")
                .OldAnnotation("Npgsql:Enum:product_status", "active,draft,archived")
                .OldAnnotation("Npgsql:Enum:review_status", "pending,approved,rejected")
                .OldAnnotation("Npgsql:Enum:sharing_type", "percentage,fixed_amount")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:uuid-ossp", ",,");
        }
    }
}
