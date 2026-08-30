using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class ProductVideoConfiguration : IEntityTypeConfiguration<ProductVideo>
{
    public void Configure(EntityTypeBuilder<ProductVideo> b)
    {
        b.ToTable("product_videos"); b.HasKey(x => x.Id);
        b.Property(x => x.YoutubeUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200);
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Product).WithMany(p => p.Videos).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
