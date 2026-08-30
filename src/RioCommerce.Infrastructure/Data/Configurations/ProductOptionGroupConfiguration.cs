using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class ProductOptionGroupConfiguration : IEntityTypeConfiguration<ProductOptionGroup>
{
    public void Configure(EntityTypeBuilder<ProductOptionGroup> b)
    {
        // Default EF convention → table "ProductOptionGroups" (matches migration 0012).
        b.HasKey(g => g.Id);
        b.Property(g => g.Name).HasMaxLength(80).IsRequired();
        b.HasIndex(g => g.ProductId);
        b.HasOne(g => g.Product).WithMany(p => p.OptionGroups).HasForeignKey(g => g.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(g => g.Items).WithOne(i => i.Group).HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductOptionGroupItemConfiguration : IEntityTypeConfiguration<ProductOptionGroupItem>
{
    public void Configure(EntityTypeBuilder<ProductOptionGroupItem> b)
    {
        b.HasKey(i => i.Id);
        b.Property(i => i.Name).HasMaxLength(120).IsRequired();
        b.Property(i => i.PriceAddOn).HasPrecision(10, 2);
        b.HasIndex(i => i.GroupId);
    }
}
