using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews"); b.HasKey(r => r.Id);
        b.Property(r => r.Comment).HasMaxLength(2000).IsRequired();
        b.Property(r => r.Title).HasMaxLength(160);
        b.Property(r => r.AuthorName).HasMaxLength(200);
        b.HasIndex(r => new { r.ProductId, r.UserId }).IsUnique();   // one review per user per product
        b.HasIndex(r => r.Status);
        b.HasOne(r => r.Product).WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
