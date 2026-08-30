using System;
using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFranchiseOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalRemarks",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Franchises",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                table: "Franchises",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessName",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentUrls",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gstin",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pan",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinCode",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAt",
                table: "Franchises",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<string>(
                name: "RejectionRemarks",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Franchises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<FranchiseStatus>(
                name: "Status",
                table: "Franchises",
                type: "franchise_status",
                nullable: false,
                defaultValue: FranchiseStatus.Approved);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "ApprovalRemarks",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "BusinessName",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "DocumentUrls",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "Gstin",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "Pan",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "PinCode",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "RejectionRemarks",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Franchises");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Franchises");

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
        }
    }
}
