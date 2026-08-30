using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class SeoUrlRecordConfiguration : IEntityTypeConfiguration<SeoUrlRecord>
{
    public void Configure(EntityTypeBuilder<SeoUrlRecord> b)
    {
        b.ToTable("seo_url_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Slug).HasMaxLength(300).IsRequired();
        b.Property(x => x.NormalizedSlug).HasMaxLength(300).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
        // Global uniqueness — one public URL = one owner. Also enforced by the SQL migration.
        b.HasIndex(x => x.NormalizedSlug).IsUnique();
        // One registry row per owner.
        b.HasIndex(x => new { x.EntityType, x.EntityId }).IsUnique();
    }
}
