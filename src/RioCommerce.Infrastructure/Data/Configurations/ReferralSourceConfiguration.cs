using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class ReferralSourceConfiguration : IEntityTypeConfiguration<ReferralSource>
{
    public void Configure(EntityTypeBuilder<ReferralSource> b)
    {
        b.ToTable("referral_sources");
        b.Property(r => r.Name).IsRequired().HasMaxLength(120);
        b.Property(r => r.ColorBadge).HasMaxLength(20);
        b.Property(r => r.Icon).HasMaxLength(40);
        b.Property(r => r.Description).HasMaxLength(500);
        b.HasIndex(r => r.Name).HasFilter("\"IsDeleted\" = false").IsUnique();
        b.HasIndex(r => new { r.IsActive, r.DisplayOrder });
        // Soft-delete: every read goes through this filter automatically.
        b.HasQueryFilter(r => !r.IsDeleted);
    }
}
