using System;
using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFranchiseCommissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:attribute_control_type", "dropdown_list,radio_list,checkboxes,text_box,multiline_text_box,datepicker")
                .Annotation("Npgsql:Enum:commission_type", "percent,fixed")
                .Annotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .Annotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .Annotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .Annotation("Npgsql:Enum:franchise_status", "approved,pending,rejected")
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
                .OldAnnotation("Npgsql:Enum:franchise_status", "approved,pending,rejected")
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
                name: "FranchiseCommissionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    FranchiseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "text", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductTitle = table.Column<string>(type: "text", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Type = table.Column<CommissionType>(type: "commission_type", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranchiseCommissionEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FranchiseCommissionEntries_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FranchiseCommissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    FranchiseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<CommissionType>(type: "commission_type", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranchiseCommissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FranchiseCommissions_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FranchiseCommissions_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseCommissionEntries_FranchiseId_EarnedAt",
                table: "FranchiseCommissionEntries",
                columns: new[] { "FranchiseId", "EarnedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseCommissions_FranchiseId_ProductId",
                table: "FranchiseCommissions",
                columns: new[] { "FranchiseId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FranchiseCommissions_ProductId",
                table: "FranchiseCommissions",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FranchiseCommissionEntries");

            migrationBuilder.DropTable(
                name: "FranchiseCommissions");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:attribute_control_type", "dropdown_list,radio_list,checkboxes,text_box,multiline_text_box,datepicker")
                .Annotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .Annotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .Annotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .Annotation("Npgsql:Enum:franchise_status", "approved,pending,rejected")
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
                .OldAnnotation("Npgsql:Enum:commission_type", "percent,fixed")
                .OldAnnotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .OldAnnotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .OldAnnotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .OldAnnotation("Npgsql:Enum:franchise_status", "approved,pending,rejected")
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
