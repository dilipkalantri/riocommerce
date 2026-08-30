using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCustomerDetails : Migration
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
                .Annotation("Npgsql:Enum:customer_type", "individual,organization")
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

            migrationBuilder.AddColumn<string>(
                name: "CustomerNotes",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<CustomerType>(
                name: "CustomerType",
                table: "orders",
                type: "customer_type",
                nullable: false,
                defaultValue: CustomerType.Individual);

            migrationBuilder.AddColumn<string>(
                name: "OrgName",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingCharges",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCity",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingPincode",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingState",
                table: "orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerNotes",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CustomerType",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "OrgName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingAddress",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingCharges",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingCity",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingPincode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingState",
                table: "orders");

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
                .OldAnnotation("Npgsql:Enum:commission_type", "percent,fixed")
                .OldAnnotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .OldAnnotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .OldAnnotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .OldAnnotation("Npgsql:Enum:customer_type", "individual,organization")
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
