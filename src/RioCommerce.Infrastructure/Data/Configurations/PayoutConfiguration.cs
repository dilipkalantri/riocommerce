using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> b)
    {
        b.ToTable("payouts"); b.HasKey(x => x.Id);
        b.Property(x => x.BeneficiaryName).HasMaxLength(200);
        b.Property(x => x.PaymentReference).HasMaxLength(120);
        b.Property(x => x.Provider).HasMaxLength(40);
        foreach (var p in new[] { "GrossAmount", "Adjustments", "RefundAdjustments", "TaxDeduction", "NetAmount" })
            b.Property<decimal>(p).HasPrecision(12, 2);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => new { x.BeneficiaryType, x.BeneficiaryId });
        b.HasIndex(x => x.SettlementBatchId);
        b.HasMany(x => x.Items).WithOne(i => i.Payout).HasForeignKey(i => i.PayoutId).OnDelete(DeleteBehavior.Cascade);
        // Batch ↔ payout link configured from the batch side (see SettlementBatchConfiguration).
    }
}

public class PayoutItemConfiguration : IEntityTypeConfiguration<PayoutItem>
{
    public void Configure(EntityTypeBuilder<PayoutItem> b)
    {
        b.ToTable("payout_items"); b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.OrderNumber).HasMaxLength(40);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.HasIndex(x => x.PayoutId);
        // Dedupe lookups when generating new batches (no double-paying the same earning line).
        b.HasIndex(x => new { x.OrderItemId, x.Source });
        b.HasIndex(x => x.RefundId);
    }
}

public class SettlementBatchConfiguration : IEntityTypeConfiguration<SettlementBatch>
{
    public void Configure(EntityTypeBuilder<SettlementBatch> b)
    {
        b.ToTable("settlement_batches"); b.HasKey(x => x.Id);
        b.Property(x => x.BatchNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Provider).HasMaxLength(40);
        b.Property(x => x.PaymentReference).HasMaxLength(120);
        b.Property(x => x.CreatedByName).HasMaxLength(200);
        foreach (var p in new[] { "TotalGross", "TotalAdjustments", "TotalRefundAdjustments", "TotalTax", "TotalNet" })
            b.Property<decimal>(p).HasPrecision(12, 2);
        b.HasIndex(x => x.BatchNumber).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasMany(x => x.Payouts).WithOne(p => p.SettlementBatch).HasForeignKey(p => p.SettlementBatchId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Adjustments).WithOne(a => a.SettlementBatch).HasForeignKey(a => a.SettlementBatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SettlementAdjustmentConfiguration : IEntityTypeConfiguration<SettlementAdjustment>
{
    public void Configure(EntityTypeBuilder<SettlementAdjustment> b)
    {
        b.ToTable("settlement_adjustments"); b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.CreatedByName).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.HasIndex(x => x.SettlementBatchId);
    }
}
