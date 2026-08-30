using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permissions");
        b.Property(x => x.PermissionKey).HasMaxLength(120).IsRequired();
        // One row per (role, key) — duplicate inserts are a no-op via the unique index.
        b.HasIndex(x => new { x.RoleId, x.PermissionKey }).IsUnique();
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> b)
    {
        b.ToTable("user_permission_overrides");
        b.Property(x => x.PermissionKey).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.UserId, x.PermissionKey }).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
