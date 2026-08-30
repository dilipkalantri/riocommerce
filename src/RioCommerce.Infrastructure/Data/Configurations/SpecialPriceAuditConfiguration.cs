using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class SpecialPriceAuditConfiguration : IEntityTypeConfiguration<SpecialPriceAudit>
{
    public void Configure(EntityTypeBuilder<SpecialPriceAudit> b)
    {
        b.ToTable("special_price_audits");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        b.Property(a => a.ProductName).HasMaxLength(500).IsRequired();
        b.Property(a => a.OldPrice).HasPrecision(10, 2);
        b.Property(a => a.NewPrice).HasPrecision(10, 2);
        b.Property(a => a.ModifiedByName).HasMaxLength(200).IsRequired();
        b.Property(a => a.ModifiedAt).HasDefaultValueSql("NOW()");
        b.Property(a => a.Remarks).HasMaxLength(500);

        b.HasIndex(a => a.ProductId);
        b.HasIndex(a => a.ModifiedAt);

        b.HasOne(a => a.Product)
            .WithMany(p => p.SpecialPriceAudits)
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
