using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <see cref="Invoice"/> onto the <c>invoices</c> table.
/// Important indexes:
///   • Unique on InvoiceNumber — defends against the rare race in number-generation
///   • Unique on OrderId WHERE Active — one live invoice per order; cancelled+reissued allowed
///   • InvoiceDate DESC for the admin list-view ordering
/// </summary>
public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(i => i.Id);

        b.Property(i => i.InvoiceNumber).HasMaxLength(40).IsRequired();
        b.Property(i => i.OrderNumber).HasMaxLength(40);
        b.Property(i => i.InvoiceDate).HasColumnType("date").IsRequired();
        b.Property(i => i.CustomerName).HasMaxLength(200);
        b.Property(i => i.CustomerEmail).HasMaxLength(200);
        b.Property(i => i.CustomerPhone).HasMaxLength(40);
        b.Property(i => i.BillingAddress).HasMaxLength(500);
        b.Property(i => i.BillingCity).HasMaxLength(120);
        b.Property(i => i.BillingState).HasMaxLength(120);
        b.Property(i => i.BillingPincode).HasMaxLength(20);
        b.Property(i => i.CustomerGstin).HasMaxLength(20);
        b.Property(i => i.GstClassification).HasMaxLength(10).IsRequired();
        b.Property(i => i.Currency).HasMaxLength(8).IsRequired();
        b.Property(i => i.PaymentReference).HasMaxLength(120);
        // Gateway-reported instrument snapshot ("UPI", "Credit Card", …) — width matches 0021.
        b.Property(i => i.GatewayPaymentMode).HasMaxLength(40);
        b.Property(i => i.Notes).HasMaxLength(2000);
        b.Property(i => i.CompanyName).HasMaxLength(200);
        b.Property(i => i.CompanyGstin).HasMaxLength(20);
        b.Property(i => i.CompanyAddress).HasMaxLength(500);
        b.Property(i => i.CompanyPhone).HasMaxLength(40);
        b.Property(i => i.CompanyEmail).HasMaxLength(200);
        b.Property(i => i.GeneratedByName).HasMaxLength(200);
        b.Property(i => i.CancelledReason).HasMaxLength(500);

        // Money — match the existing Order/OrderItem precision exactly.
        b.Property(i => i.Subtotal).HasColumnType("numeric(18,2)");
        b.Property(i => i.DiscountAmount).HasColumnType("numeric(18,2)");
        b.Property(i => i.TaxableAmount).HasColumnType("numeric(18,2)");
        b.Property(i => i.CgstAmount).HasColumnType("numeric(18,2)");
        b.Property(i => i.SgstAmount).HasColumnType("numeric(18,2)");
        b.Property(i => i.IgstAmount).HasColumnType("numeric(18,2)");
        b.Property(i => i.ShippingCharges).HasColumnType("numeric(18,2)");
        b.Property(i => i.TotalAmount).HasColumnType("numeric(18,2)");

        // InvoiceStatus is a plain .NET enum — NOT registered as a Postgres custom enum
        // (unlike PaymentMode, OrderStatus, etc.). Force EF to store it as a plain integer
        // column so we don't accidentally pick up the npgsql enum-binder.
        b.Property(i => i.Status).HasConversion<int>().HasColumnType("integer");

        b.HasIndex(i => i.InvoiceNumber).IsUnique();

        // Uniqueness of the live invoice is enforced by IX_invoices_Order_Student_Active, created
        // in 0048_school_enrollment_payment.sql. It is an EXPRESSION index —
        //     UNIQUE ("OrderId", COALESCE("StudentUserId", <sentinel>)) WHERE "Status" = 0
        // — which EF's HasIndex cannot model, so it is declared in SQL only. That is consistent
        // with this repo: migration scripts own the schema, the DbContext only queries it
        // (see README). Declaring a plain unique HasIndex here instead would be WRONG twice over:
        // it would still block the second student's invoice, and PostgreSQL treats NULLs as
        // distinct, so a two-column version would quietly let ordinary orders have several.
        b.HasIndex(i => i.StudentUserId).HasDatabaseName("IX_invoices_StudentUserId");
        b.HasIndex(i => i.InvoiceDate).HasDatabaseName("IX_invoices_InvoiceDate");

        b.HasOne(i => i.Order)
            .WithMany()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(i => i.LineItems)
            .WithOne(l => l.Invoice!)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> b)
    {
        b.ToTable("invoice_line_items");
        b.HasKey(l => l.Id);

        b.Property(l => l.Description).HasMaxLength(500).IsRequired();
        b.Property(l => l.ModeName).HasMaxLength(120);
        b.Property(l => l.HsnCode).HasMaxLength(20);

        b.Property(l => l.UnitPrice).HasColumnType("numeric(18,2)");
        b.Property(l => l.Discount).HasColumnType("numeric(18,2)");
        b.Property(l => l.GstRate).HasColumnType("numeric(5,2)");
        b.Property(l => l.GstAmount).HasColumnType("numeric(18,2)");
        b.Property(l => l.LineTotal).HasColumnType("numeric(18,2)");

        b.HasIndex(l => l.InvoiceId).HasDatabaseName("IX_invoice_line_items_InvoiceId");
    }
}
