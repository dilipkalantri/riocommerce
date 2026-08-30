using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping for the Order Management &amp; Reporting module tables. Table/column names match
/// <c>Migrations/Scripts/0028_order_reporting_module.sql</c>, which is the authority — EF
/// migrations are not used in this project (see <c>SqlMigrationRunner</c>).
///
/// <para>Status enums here are plain .NET enums, NOT registered as Postgres custom enums, so each
/// is pinned to an explicit storage type. Following <c>InvoiceConfiguration</c>: integer columns
/// for the new tables, and a string column for <see cref="ShipmentStatus"/> because it replaces an
/// existing <c>varchar(20)</c> whose values already spell the enum names exactly — converting the
/// live column to integer would buy nothing and risk the data.</para>
/// </summary>
public class FranchiseReInvoiceConfiguration : IEntityTypeConfiguration<FranchiseReInvoice>
{
    public void Configure(EntityTypeBuilder<FranchiseReInvoice> b)
    {
        b.ToTable("franchise_reinvoices");
        b.HasKey(r => r.Id);

        b.Property(r => r.ReInvoiceNumber).HasMaxLength(50).IsRequired();
        b.Property(r => r.OriginalInvoiceNumber).HasMaxLength(50).IsRequired();
        b.Property(r => r.OrderNumber).HasMaxLength(50).IsRequired();
        b.Property(r => r.FranchiseName).HasMaxLength(200).IsRequired();
        b.Property(r => r.FranchiseCode).HasMaxLength(40);
        b.Property(r => r.FranchiseGstin).HasMaxLength(20);
        b.Property(r => r.PlaceOfSupply).HasMaxLength(100);
        b.Property(r => r.Currency).HasMaxLength(3);
        b.Property(r => r.CancelledReason).HasMaxLength(500);
        b.Property(r => r.IssuedByName).HasMaxLength(200);
        b.Property(r => r.Notes).HasMaxLength(1000);

        b.Property(r => r.TaxableAmount).HasPrecision(12, 2);
        b.Property(r => r.CgstAmount).HasPrecision(12, 2);
        b.Property(r => r.SgstAmount).HasPrecision(12, 2);
        b.Property(r => r.IgstAmount).HasPrecision(12, 2);
        b.Property(r => r.TotalGst).HasPrecision(12, 2);
        b.Property(r => r.TotalAmount).HasPrecision(12, 2);
        b.Property(r => r.GstRate).HasPrecision(5, 2);
        b.Property(r => r.FranchiseShareAmount).HasPrecision(12, 2);

        b.Property(r => r.Status).HasConversion<int>().HasColumnType("integer");

        b.HasIndex(r => r.ReInvoiceNumber).IsUnique();
        b.HasIndex(r => r.OriginalInvoiceId);
        b.HasIndex(r => r.FranchiseId);
        b.HasIndex(r => r.ReInvoiceDate);
        b.HasIndex(r => r.OrderId);

        // One LIVE re-invoice per original. Filtered on status so cancelling one frees the original
        // to be re-invoiced — the whole correction path for a document that is never edited.
        //
        // Declared with an explicit NAME. Calling HasIndex on a property that already has an index
        // returns the SAME builder, so without a distinct name this would silently turn the plain
        // OriginalInvoiceId index above into a unique filtered one instead of adding a second index,
        // and EF's model would then disagree with what migration 0028 actually creates.
        b.HasIndex(new[] { nameof(FranchiseReInvoice.OriginalInvoiceId) },
                   "IX_franchise_reinvoices_Original_Active")
            .IsUnique()
            .HasFilter("\"Status\" <> 2");

        b.HasOne(r => r.OriginalInvoice).WithMany()
            .HasForeignKey(r => r.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Franchise).WithMany()
            .HasForeignKey(r => r.FranchiseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FranchiseReInvoiceItemConfiguration : IEntityTypeConfiguration<FranchiseReInvoiceItem>
{
    public void Configure(EntityTypeBuilder<FranchiseReInvoiceItem> b)
    {
        b.ToTable("franchise_reinvoice_items");
        b.HasKey(i => i.Id);

        b.Property(i => i.ProductTitle).HasMaxLength(500).IsRequired();
        b.Property(i => i.SubjectName).HasMaxLength(200);
        b.Property(i => i.UnitPrice).HasPrecision(12, 2);
        b.Property(i => i.TaxableAmount).HasPrecision(12, 2);
        b.Property(i => i.GstRate).HasPrecision(5, 2);
        b.Property(i => i.CgstAmount).HasPrecision(12, 2);
        b.Property(i => i.SgstAmount).HasPrecision(12, 2);
        b.Property(i => i.IgstAmount).HasPrecision(12, 2);
        b.Property(i => i.GstAmount).HasPrecision(12, 2);
        b.Property(i => i.LineTotal).HasPrecision(12, 2);

        b.HasIndex(i => i.ReInvoiceId);
        b.HasIndex(i => i.ProductId);
        b.HasIndex(i => i.SubjectId);

        b.HasOne(i => i.ReInvoice).WithMany(r => r.Items)
            .HasForeignKey(i => i.ReInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherSettlementConfiguration : IEntityTypeConfiguration<TeacherSettlement>
{
    public void Configure(EntityTypeBuilder<TeacherSettlement> b)
    {
        b.ToTable("teacher_settlements");
        b.HasKey(s => s.Id);

        b.Property(s => s.SettlementNumber).HasMaxLength(50).IsRequired();
        b.Property(s => s.FacultyName).HasMaxLength(200).IsRequired();
        b.Property(s => s.PaymentReference).HasMaxLength(120);
        b.Property(s => s.Notes).HasMaxLength(1000);
        b.Property(s => s.CreatedByName).HasMaxLength(200);

        b.Property(s => s.TotalSales).HasPrecision(14, 2);
        b.Property(s => s.ShareAmount).HasPrecision(12, 2);
        b.Property(s => s.GstOnShare).HasPrecision(12, 2);
        b.Property(s => s.TotalPayable).HasPrecision(12, 2);
        b.Property(s => s.TdsDeduction).HasPrecision(12, 2);
        b.Property(s => s.Adjustments).HasPrecision(12, 2);
        b.Property(s => s.AmountPaid).HasPrecision(12, 2);
        b.Property(s => s.BalancePayable).HasPrecision(12, 2);

        b.Property(s => s.Status).HasConversion<int>().HasColumnType("integer");

        b.HasIndex(s => s.SettlementNumber).IsUnique();
        b.HasIndex(s => s.FacultyId);
        b.HasIndex(s => s.Status);
        b.HasIndex(s => new { s.PeriodStartUtc, s.PeriodEndUtc });
        // One settlement per faculty per period — regenerating a period must update the existing
        // row, never silently create a second one that double-counts the payable.
        b.HasIndex(s => new { s.FacultyId, s.PeriodStartUtc, s.PeriodEndUtc }).IsUnique();

        b.HasOne(s => s.Faculty).WithMany()
            .HasForeignKey(s => s.FacultyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TeacherSettlementItemConfiguration : IEntityTypeConfiguration<TeacherSettlementItem>
{
    public void Configure(EntityTypeBuilder<TeacherSettlementItem> b)
    {
        b.ToTable("teacher_settlement_items");
        b.HasKey(i => i.Id);

        b.Property(i => i.ProductTitle).HasMaxLength(500).IsRequired();
        b.Property(i => i.SubjectName).HasMaxLength(200);
        b.Property(i => i.GrossSales).HasPrecision(14, 2);
        b.Property(i => i.TaxableBase).HasPrecision(14, 2);
        b.Property(i => i.ShareValue).HasPrecision(10, 2);
        b.Property(i => i.ShareAmount).HasPrecision(12, 2);
        b.Property(i => i.GstOnShare).HasPrecision(12, 2);
        b.Property(i => i.TotalPayout).HasPrecision(12, 2);

        b.HasIndex(i => i.SettlementId);
        b.HasIndex(i => i.ProductId);
        b.HasIndex(i => i.SubjectId);

        b.HasOne(i => i.Settlement).WithMany(s => s.Items)
            .HasForeignKey(i => i.SettlementId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeacherSettlementPaymentConfiguration : IEntityTypeConfiguration<TeacherSettlementPayment>
{
    public void Configure(EntityTypeBuilder<TeacherSettlementPayment> b)
    {
        b.ToTable("teacher_settlement_payments");
        b.HasKey(p => p.Id);

        b.Property(p => p.Amount).HasPrecision(12, 2);
        b.Property(p => p.Reference).HasMaxLength(120);
        b.Property(p => p.Notes).HasMaxLength(500);
        b.Property(p => p.RecordedByName).HasMaxLength(200);

        b.HasIndex(p => p.SettlementId);

        b.HasOne(p => p.Settlement).WithMany(s => s.Payments)
            .HasForeignKey(p => p.SettlementId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
{
    public void Configure(EntityTypeBuilder<ShipmentItem> b)
    {
        b.ToTable("shipment_items");
        b.HasKey(i => i.Id);

        b.Property(i => i.ProductTitle).HasMaxLength(500).IsRequired();

        b.HasIndex(i => i.ShipmentId);
        b.HasIndex(i => i.ProductId);

        b.HasOne(i => i.Shipment).WithMany(s => s.Items)
            .HasForeignKey(i => i.ShipmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
