using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class PermissionAuditLogConfiguration : IEntityTypeConfiguration<PermissionAuditLog>
{
    public void Configure(EntityTypeBuilder<PermissionAuditLog> b)
    {
        b.ToTable("permission_audit_logs");
        b.Property(x => x.PermissionKey).HasMaxLength(120).IsRequired();
        b.Property(x => x.ChangedByName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Action).HasMaxLength(40).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(64);
        // History panel queries — paged by user, newest-first; filters use these columns.
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => x.PermissionKey);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
