using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class ProductRecommendationConfiguration : IEntityTypeConfiguration<ProductRecommendation>
{
    public void Configure(EntityTypeBuilder<ProductRecommendation> b)
    {
        b.ToTable("product_recommendations");
        b.Property(r => r.CustomTitle).HasMaxLength(120);
        b.Property(r => r.BadgeText).HasMaxLength(40);
        b.Property(r => r.BadgeColor).HasMaxLength(20);

        // Lookups are always "all active recommendations for this source product".
        b.HasIndex(r => new { r.ProductId, r.IsActive, r.Priority, r.DisplayOrder });
        // One row per (source, target) pair on the live (non-deleted) set.
        b.HasIndex(r => new { r.ProductId, r.RecommendedProductId })
            .HasFilter("\"IsDeleted\" = false")
            .IsUnique();

        // Forward navigations to source + recommended product — DO NOT cascade. If a Product is hard-deleted
        // we explicitly clean up these rows first; cascade would also blow away the source's other rows.
        b.HasOne(r => r.Product).WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.RecommendedProduct).WithMany().HasForeignKey(r => r.RecommendedProductId).OnDelete(DeleteBehavior.Restrict);

        b.HasQueryFilter(r => !r.IsDeleted);
    }
}
