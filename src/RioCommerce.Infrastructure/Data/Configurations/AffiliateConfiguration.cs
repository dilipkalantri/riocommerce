using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class AffiliateConfiguration : IEntityTypeConfiguration<Affiliate>
{
    public void Configure(EntityTypeBuilder<Affiliate> b)
    {
        b.ToTable("affiliates"); b.HasKey(a => a.Id);
        b.Property(a => a.Name).HasMaxLength(200).IsRequired();
        b.Property(a => a.Code).HasMaxLength(40).IsRequired();
        b.HasIndex(a => a.Code).IsUnique();
        b.Property(a => a.CommissionValue).HasPrecision(12, 2);
        b.Property(a => a.TotalEarned).HasPrecision(12, 2);
        b.Property(a => a.TotalPaid).HasPrecision(12, 2);
        b.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId);
    }
}

public class AffiliateReferralConfiguration : IEntityTypeConfiguration<AffiliateReferral>
{
    public void Configure(EntityTypeBuilder<AffiliateReferral> b)
    {
        b.ToTable("affiliate_referrals"); b.HasKey(r => r.Id);
        b.Property(r => r.OrderNumber).HasMaxLength(20);
        b.Property(r => r.OrderAmount).HasPrecision(12, 2);
        b.Property(r => r.Commission).HasPrecision(12, 2);
        b.HasIndex(r => r.OrderId).IsUnique();   // one commission record per order
        b.HasOne(r => r.Affiliate).WithMany(a => a.Referrals).HasForeignKey(r => r.AffiliateId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.Order).WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
