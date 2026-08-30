using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs"); b.HasKey(a => a.Id);
        b.Property(a => a.ActorName).HasMaxLength(200);
        b.Property(a => a.Module).HasMaxLength(40);
        b.Property(a => a.Action).HasMaxLength(80).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(60);
        b.Property(a => a.EntityId).HasMaxLength(60);
        b.Property(a => a.EntityName).HasMaxLength(200);
        b.Property(a => a.OldValue).HasMaxLength(1000);
        b.Property(a => a.NewValue).HasMaxLength(1000);
        b.Property(a => a.Status).HasMaxLength(20);
        b.Property(a => a.IpAddress).HasMaxLength(64);
        b.Property(a => a.UserAgent).HasMaxLength(400);
        b.Property(a => a.Browser).HasMaxLength(40);
        b.Property(a => a.Device).HasMaxLength(40);
        // Audit details often carry full JSON payloads — relax the column cap to text.
        b.Property(a => a.Details).HasColumnType("text");
        // Indexes the audit page uses: newest-first table, module filter, actor filter, action filter.
        b.HasIndex(a => a.CreatedAt);
        b.HasIndex(a => a.Module);
        b.HasIndex(a => a.ActorUserId);
        b.HasIndex(a => a.Action);
    }
}
