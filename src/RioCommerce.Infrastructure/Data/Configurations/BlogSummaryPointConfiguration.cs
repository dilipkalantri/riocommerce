using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class BlogSummaryPointConfiguration : IEntityTypeConfiguration<BlogSummaryPoint>
{
    public void Configure(EntityTypeBuilder<BlogSummaryPoint> b)
    {
        b.ToTable("BlogSummaryPoints"); b.HasKey(p => p.Id);
        b.Property(p => p.Title).HasMaxLength(200).IsRequired();
        b.Property(p => p.Description).HasMaxLength(600).IsRequired();

        // A post cannot have two "point 3"s. Also the read path's sort key, so this
        // index serves the ordered load as well as the constraint.
        b.HasIndex(p => new { p.BlogPostId, p.PointNumber }).IsUnique();

        // Cascade: the points are meaningless without their post, and deleting a post
        // already deletes its content. Matches the FK in migration 0027.
        b.HasOne(p => p.BlogPost)
         .WithMany(x => x.SummaryPoints)
         .HasForeignKey(p => p.BlogPostId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
