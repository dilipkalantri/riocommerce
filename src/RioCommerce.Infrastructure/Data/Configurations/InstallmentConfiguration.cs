using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class OrderInstallmentPlanConfiguration : IEntityTypeConfiguration<OrderInstallmentPlan>
{
    public void Configure(EntityTypeBuilder<OrderInstallmentPlan> b)
    {
        b.ToTable("order_installment_plans");
        b.HasKey(p => p.Id);
        b.Property(p => p.TotalAmount).HasPrecision(12, 2);
        b.Property(p => p.DownPayment).HasPrecision(12, 2);
        b.Property(p => p.PaidAmount).HasPrecision(12, 2);
        b.Ignore(p => p.OutstandingAmount);   // computed
        b.HasIndex(p => p.OrderId).IsUnique();
        b.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Installments).WithOne(i => i.Plan).HasForeignKey(i => i.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderInstallmentConfiguration : IEntityTypeConfiguration<OrderInstallment>
{
    public void Configure(EntityTypeBuilder<OrderInstallment> b)
    {
        b.ToTable("order_installments");
        b.HasKey(i => i.Id);
        b.Property(i => i.Amount).HasPrecision(12, 2);
        b.Property(i => i.PaidAmount).HasPrecision(12, 2);
        b.Property(i => i.PaymentReference).HasMaxLength(200);
        b.Ignore(i => i.PendingAmount);       // computed
        b.HasIndex(i => new { i.PlanId, i.InstallmentNumber });
        b.HasIndex(i => new { i.Status, i.DueDate });
        b.HasIndex(i => i.OrderId);
    }
}

public class InstallmentSettingsConfiguration : IEntityTypeConfiguration<InstallmentSettings>
{
    public void Configure(EntityTypeBuilder<InstallmentSettings> b)
    {
        b.ToTable("installment_settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.MinDownPaymentPercent).HasPrecision(5, 2);
    }
}
