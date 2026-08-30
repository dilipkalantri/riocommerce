using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping for the product faculty share tables. Table/column names match
/// <c>Migrations/Scripts/0027_faculty_share.sql</c>, which is the authority — EF migrations are not
/// used in this project (see <c>SqlMigrationRunner</c>).
/// </summary>
public class FacultySettingsConfiguration : IEntityTypeConfiguration<FacultySettings>
{
    public void Configure(EntityTypeBuilder<FacultySettings> b)
    {
        b.ToTable("FacultySettings");
        b.HasKey(s => s.Id);
        b.Property(s => s.MaxTotalSharePct).HasPrecision(5, 2);
    }
}

public class FacultyShareEntryConfiguration : IEntityTypeConfiguration<FacultyShareEntry>
{
    public void Configure(EntityTypeBuilder<FacultyShareEntry> b)
    {
        b.ToTable("FacultyShareEntries");
        b.HasKey(e => e.Id);

        b.Property(e => e.OrderNumber).HasMaxLength(50).IsRequired();
        b.Property(e => e.ProductTitle).HasMaxLength(500).IsRequired();
        b.Property(e => e.BaseAmount).HasPrecision(12, 2);
        b.Property(e => e.ShareValue).HasPrecision(10, 2);
        b.Property(e => e.ShareAmount).HasPrecision(12, 2);
        b.Property(e => e.GstOnShare).HasPrecision(12, 2);
        b.Property(e => e.TotalPayout).HasPrecision(12, 2);

        b.HasIndex(e => e.FacultyId);
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => e.EarnedAt);
        // Makes the order-confirmation ledger write idempotent: a retried confirmation cannot
        // double-credit the same faculty for the same line.
        b.HasIndex(e => new { e.OrderItemId, e.FacultyId }).IsUnique();

        b.HasOne(e => e.Faculty).WithMany().HasForeignKey(e => e.FacultyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FacultySharingRuleConfiguration : IEntityTypeConfiguration<FacultySharingRule>
{
    public void Configure(EntityTypeBuilder<FacultySharingRule> b)
    {
        b.ToTable("FacultySharingRules");
        b.HasKey(r => r.Id);
        b.Property(r => r.ShareValue).HasPrecision(10, 2);
        b.Property(r => r.EffectiveAmount).HasPrecision(12, 2);
        // One rule per (product, faculty) — the payout would otherwise double-count the pair. The
        // SQL script creates the same index, skipping it if legacy duplicates exist.
        b.HasIndex(r => new { r.ProductId, r.FacultyId }).IsUnique();
    }
}
