using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

/// <summary>
/// Re-declares the unique index on (BookPreviewId, PageNumber) that the original
/// AddBookPreviewPages migration created but no configuration class ever owned.
/// Without this, EF's model snapshot and the model code drift, and every new
/// migration generated against this DbContext includes an unwanted DropIndex
/// operation for IX_BookPreviewPages_BookPreviewId_PageNumber.
/// </summary>
public class BookPreviewPageConfiguration : IEntityTypeConfiguration<BookPreviewPage>
{
    public void Configure(EntityTypeBuilder<BookPreviewPage> b)
    {
        b.ToTable("BookPreviewPages");
        b.HasKey(p => p.Id);

        // One row per (preview, page) — enforced as unique so re-uploads can't double-insert.
        b.HasIndex(p => new { p.BookPreviewId, p.PageNumber }).IsUnique();

        // CRITICAL: link to ProductBookPreview.Pages so EF sees ONE relationship, not two.
        // Without WithMany(bp => bp.Pages) EF treats the principal-side collection as a separate
        // relationship and auto-generates a shadow FK "ProductBookPreviewId" — which then conflicts
        // with the explicit FK "BookPreviewId" and produces phantom migration operations.
        b.HasOne(p => p.BookPreview)
            .WithMany(bp => bp.Pages)
            .HasForeignKey(p => p.BookPreviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
