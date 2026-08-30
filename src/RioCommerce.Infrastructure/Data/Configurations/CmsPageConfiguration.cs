using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class CmsPageConfiguration : IEntityTypeConfiguration<CmsPage>
{
    public void Configure(EntityTypeBuilder<CmsPage> b)
    {
        b.ToTable("cms_pages"); b.HasKey(p => p.Id);
        b.Property(p => p.Title).HasMaxLength(200).IsRequired();
        b.Property(p => p.Slug).HasMaxLength(160).IsRequired();
        b.HasIndex(p => p.Slug).IsUnique();
        // Unlimited length (maps to Postgres `text`) — custom HTML/CSS designs can be large.
        // The actual column is widened by migration 0018_widen_cms_body.sql.
    }
}
