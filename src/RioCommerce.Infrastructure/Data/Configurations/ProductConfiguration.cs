using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products"); b.HasKey(p => p.Id);
        b.Property(p => p.Title).HasMaxLength(500).IsRequired();
        b.Property(p => p.Slug).HasMaxLength(300).IsRequired();
        b.Property(p => p.Mrp).HasPrecision(10, 2);
        b.Property(p => p.SellingPrice).HasPrecision(10, 2);
        b.Property(p => p.DefaultFranchiseShareValue).HasPrecision(10, 2);
        b.Property(p => p.DefaultFranchiseShareType).HasConversion<int>();
        b.Property(p => p.GstRate).HasPrecision(4, 2);
        b.Property(p => p.AvgRating).HasPrecision(2, 1);
        b.Property(p => p.SpecialPrice).HasPrecision(10, 2);
        b.HasIndex(p => p.Slug).IsUnique();
        b.HasIndex(p => p.Status);
        b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
        b.HasOne(p => p.Subject).WithMany(s => s.Products).HasForeignKey(p => p.SubjectId);
        b.HasOne(p => p.PrimaryFaculty).WithMany(f => f.PrimaryProducts).HasForeignKey(p => p.PrimaryFacultyId);
    }
}

/// <summary>Every subject a product covers. The pair is the fact, hence the unique index —
/// a repeated save must not be able to link the same subject twice.</summary>
public class ProductSubjectConfiguration : IEntityTypeConfiguration<ProductSubject>
{
    public void Configure(EntityTypeBuilder<ProductSubject> b)
    {
        b.ToTable("ProductSubjects"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ProductId, x.SubjectId }).IsUnique();
        b.HasIndex(x => x.SubjectId);
        b.HasOne(x => x.Product).WithMany(p => p.ProductSubject)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        // Restrict mirrors the FK on products.SubjectId: retiring a subject is a deactivation,
        // never a delete that would silently unlink live courses.
        b.HasOne(x => x.Subject).WithMany()
            .HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
