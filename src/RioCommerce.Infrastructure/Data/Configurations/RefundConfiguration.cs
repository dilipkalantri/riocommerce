using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> b)
    {
        b.ToTable("payment_transactions"); b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.Gateway).HasMaxLength(60);
        b.Property(x => x.Currency).HasMaxLength(8);
        b.Property(x => x.GatewayTransactionId).HasMaxLength(200);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.HasIndex(x => x.OrderId);
        b.HasOne(x => x.Order).WithMany(o => o.Transactions).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> b)
    {
        b.ToTable("refunds"); b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.RefundType).HasMaxLength(20);
        b.HasIndex(x => x.OrderId);
        b.HasOne(x => x.Order).WithMany(o => o.Refunds).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Items).WithOne(i => i.Refund).HasForeignKey(i => i.RefundId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefundItemConfiguration : IEntityTypeConfiguration<RefundItem>
{
    public void Configure(EntityTypeBuilder<RefundItem> b)
    {
        b.ToTable("refund_items"); b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.ProductTitle).HasMaxLength(500);
        b.HasIndex(x => x.OrderItemId);
    }
}
