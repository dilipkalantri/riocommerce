using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <see cref="AppLog"/> onto the <c>app_logs</c> table (defined in the script-based
/// migration baseline — this project uses SQL migration scripts, not EF migrations).
///
/// Indexes are deliberate:
///   • (Level DESC, CreatedAt DESC) — "show me the most recent errors" page query
///   • (Category, CreatedAt DESC)   — filter-by-category view
///   • (OrderId)                    — "all logs for order X" — used from the order detail page
///   • (UserId)                     — partial, only when present
/// </summary>
public class AppLogConfiguration : IEntityTypeConfiguration<AppLog>
{
    public void Configure(EntityTypeBuilder<AppLog> b)
    {
        b.ToTable("app_logs");
        b.HasKey(l => l.Id);

        b.Property(l => l.Level).HasConversion<int>().IsRequired();
        b.Property(l => l.Category).HasMaxLength(120).IsRequired();
        b.Property(l => l.EventCode).HasMaxLength(80);
        b.Property(l => l.Message).HasMaxLength(2000).IsRequired();
        b.Property(l => l.Exception).HasColumnType("text");
        b.Property(l => l.Properties).HasColumnType("jsonb");
        b.Property(l => l.EntityType).HasMaxLength(60);
        b.Property(l => l.EntityId).HasMaxLength(80);
        b.Property(l => l.RequestPath).HasMaxLength(500);
        b.Property(l => l.Source).HasMaxLength(60);
        b.Property(l => l.CreatedAt).HasDefaultValueSql("NOW()");

        b.HasIndex(l => new { l.Level, l.CreatedAt }).HasDatabaseName("IX_app_logs_Level_CreatedAt");
        b.HasIndex(l => new { l.Category, l.CreatedAt }).HasDatabaseName("IX_app_logs_Category_CreatedAt");
        b.HasIndex(l => l.OrderId).HasDatabaseName("IX_app_logs_OrderId").HasFilter("\"OrderId\" IS NOT NULL");
        b.HasIndex(l => l.UserId).HasDatabaseName("IX_app_logs_UserId").HasFilter("\"UserId\" IS NOT NULL");
    }
}
