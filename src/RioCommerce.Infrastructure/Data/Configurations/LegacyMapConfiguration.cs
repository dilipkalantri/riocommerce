using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class LegacyProductMapConfiguration : IEntityTypeConfiguration<LegacyProductMap>
{
    public void Configure(EntityTypeBuilder<LegacyProductMap> b)
    {
        b.ToTable("legacy_product_map");
        b.HasKey(x => x.Id);
        b.Property(x => x.LegacyKey).HasMaxLength(400);
        b.HasIndex(x => x.LegacyId).IsUnique();
        b.HasIndex(x => x.NewId);
    }
}

public class LegacyUserMapConfiguration : IEntityTypeConfiguration<LegacyUserMap>
{
    public void Configure(EntityTypeBuilder<LegacyUserMap> b)
    {
        b.ToTable("legacy_user_map");
        b.HasKey(x => x.Id);
        b.Property(x => x.LegacyKey).HasMaxLength(1000);
        b.HasIndex(x => x.LegacyId).IsUnique();
        b.HasIndex(x => x.NewId);
    }
}
