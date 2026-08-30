using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

// ── Product attributes (reusable definitions + predefined values) ──
public class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> b)
    {
        b.ToTable("product_attributes"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasMany(x => x.PredefinedValues).WithOne(v => v.ProductAttribute)
            .HasForeignKey(v => v.ProductAttributeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PredefinedProductAttributeValueConfiguration : IEntityTypeConfiguration<PredefinedProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<PredefinedProductAttributeValue> b)
    {
        b.ToTable("predefined_product_attribute_values"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.PriceAdjustment).HasPrecision(10, 2);
    }
}

// ── Per-product attribute mappings + their values ──
public class ProductAttributeMappingConfiguration : IEntityTypeConfiguration<ProductAttributeMapping>
{
    public void Configure(EntityTypeBuilder<ProductAttributeMapping> b)
    {
        b.ToTable("product_attribute_mappings"); b.HasKey(x => x.Id);
        b.Property(x => x.TextPrompt).HasMaxLength(400);
        b.Property(x => x.DefaultValue).HasMaxLength(1000);
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Product).WithMany(p => p.AttributeMappings)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        // Restrict: an attribute in use by a product can't be hard-deleted (service surfaces a friendly error).
        b.HasOne(x => x.ProductAttribute).WithMany()
            .HasForeignKey(x => x.ProductAttributeId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Values).WithOne(v => v.ProductAttributeMapping)
            .HasForeignKey(v => v.ProductAttributeMappingId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> b)
    {
        b.ToTable("product_attribute_values"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.PriceAdjustment).HasPrecision(10, 2);
    }
}

// ── Specification attributes (groups → attributes → options → product mappings) ──
public class SpecificationAttributeGroupConfiguration : IEntityTypeConfiguration<SpecificationAttributeGroup>
{
    public void Configure(EntityTypeBuilder<SpecificationAttributeGroup> b)
    {
        b.ToTable("specification_attribute_groups"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.HasMany(x => x.SpecificationAttributes).WithOne(a => a.Group)
            .HasForeignKey(a => a.SpecificationAttributeGroupId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SpecificationAttributeConfiguration : IEntityTypeConfiguration<SpecificationAttribute>
{
    public void Configure(EntityTypeBuilder<SpecificationAttribute> b)
    {
        b.ToTable("specification_attributes"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.HasMany(x => x.Options).WithOne(o => o.SpecificationAttribute)
            .HasForeignKey(o => o.SpecificationAttributeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SpecificationAttributeOptionConfiguration : IEntityTypeConfiguration<SpecificationAttributeOption>
{
    public void Configure(EntityTypeBuilder<SpecificationAttributeOption> b)
    {
        b.ToTable("specification_attribute_options"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.ColorSquaresRgb).HasMaxLength(7);
        b.HasMany(x => x.ProductMappings).WithOne(m => m.SpecificationAttributeOption)
            .HasForeignKey(m => m.SpecificationAttributeOptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductSpecificationAttributeConfiguration : IEntityTypeConfiguration<ProductSpecificationAttribute>
{
    public void Configure(EntityTypeBuilder<ProductSpecificationAttribute> b)
    {
        b.ToTable("product_specification_attributes"); b.HasKey(x => x.Id);
        b.HasIndex(x => x.ProductId);
        b.HasIndex(x => x.SpecificationAttributeOptionId);
        b.HasOne(x => x.Product).WithMany(p => p.SpecificationAttributes)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

// ── Checkout attributes + their values ──
public class CheckoutAttributeConfiguration : IEntityTypeConfiguration<CheckoutAttribute>
{
    public void Configure(EntityTypeBuilder<CheckoutAttribute> b)
    {
        b.ToTable("checkout_attributes"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.TextPrompt).HasMaxLength(400);
        b.HasMany(x => x.Values).WithOne(v => v.CheckoutAttribute)
            .HasForeignKey(v => v.CheckoutAttributeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CheckoutAttributeValueConfiguration : IEntityTypeConfiguration<CheckoutAttributeValue>
{
    public void Configure(EntityTypeBuilder<CheckoutAttributeValue> b)
    {
        b.ToTable("checkout_attribute_values"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(400).IsRequired();
        b.Property(x => x.PriceAdjustment).HasPrecision(10, 2);
    }
}
