using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders"); b.HasKey(o => o.Id);
        b.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        b.Property(o => o.StudentName).HasMaxLength(200).IsRequired();
        b.Property(o => o.StudentPhone).HasMaxLength(15).IsRequired();
        b.Property(o => o.Subtotal).HasPrecision(12, 2);
        b.Property(o => o.TotalAmount).HasPrecision(12, 2);
        b.Property(o => o.DiscountAmount).HasPrecision(12, 2);
        b.Property(o => o.GstAmount).HasPrecision(12, 2);
        b.Property(o => o.CgstAmount).HasPrecision(10, 2);
        b.Property(o => o.SgstAmount).HasPrecision(10, 2);
        b.Property(o => o.IgstAmount).HasPrecision(10, 2);
        b.Property(o => o.CheckoutAttributesAmount).HasPrecision(10, 2);
        // Franchise commission split — matches the numeric(12,2) columns added in 0025.
        b.Property(o => o.FranchiseCommissionBase).HasPrecision(12, 2);
        b.Property(o => o.FranchiseCommissionGst).HasPrecision(12, 2);
        // Gateway-reported instrument ("UPI", "Credit Card", …) — free text, not an enum, so a new
        // instrument a gateway starts reporting needs no schema change. Width matches 0021.
        b.Property(o => o.GatewayPaymentMode).HasMaxLength(40);
        b.HasIndex(o => o.OrderNumber).IsUnique();
        b.HasIndex(o => o.Status);
        b.HasIndex(o => o.IsDeleted);
        b.HasOne(o => o.User).WithMany(u => u.Orders).HasForeignKey(o => o.UserId);
        b.HasOne(o => o.Franchise).WithMany(f => f.Orders).HasForeignKey(o => o.FranchiseId);
        // One-to-MANY since 0048: a school enrolment order carries one invoice per student.
        // As a one-to-one, EF severed the first invoice's OrderId the moment a second was added
        // for the same order — the feature could not have worked at runtime. Uniqueness for
        // ordinary orders is still absolute, enforced by IX_invoices_Order_Student_Active.
        b.HasMany(o => o.Invoices)
            .WithOne(i => i.Order!)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(o => o.Notes).WithOne(n => n.Order).HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Cascade);
        // Soft delete: deleted orders are invisible everywhere unless IgnoreQueryFilters() is used.
        b.HasQueryFilter(o => !o.IsDeleted);
    }
}

public class OrderNoteConfiguration : IEntityTypeConfiguration<OrderNote>
{
    public void Configure(EntityTypeBuilder<OrderNote> b)
    {
        b.ToTable("order_notes"); b.HasKey(n => n.Id);
        b.Property(n => n.Body).HasMaxLength(4000).IsRequired();
        b.Property(n => n.CreatedByName).HasMaxLength(200);
        b.HasIndex(n => n.OrderId);
        b.HasMany(n => n.Attachments).WithOne(a => a.OrderNote).HasForeignKey(a => a.OrderNoteId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NoteAttachmentConfiguration : IEntityTypeConfiguration<NoteAttachment>
{
    public void Configure(EntityTypeBuilder<NoteAttachment> b)
    {
        b.ToTable("note_attachments"); b.HasKey(a => a.Id);
        b.Property(a => a.FileName).HasMaxLength(120).IsRequired();
        b.Property(a => a.OriginalFileName).HasMaxLength(300).IsRequired();
        b.Property(a => a.ContentType).HasMaxLength(150);
        b.Property(a => a.StoragePath).HasMaxLength(500).IsRequired();
        b.Property(a => a.HashChecksum).HasMaxLength(80);
        b.Property(a => a.VirusScanStatus).HasMaxLength(30);
        b.HasIndex(a => a.OrderId);
        b.HasIndex(a => a.OrderNoteId);
    }
}
