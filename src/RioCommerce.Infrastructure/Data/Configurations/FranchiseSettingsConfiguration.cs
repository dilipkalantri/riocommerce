using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class FranchiseSettingsConfiguration : IEntityTypeConfiguration<FranchiseSettings>
{
    public void Configure(EntityTypeBuilder<FranchiseSettings> b)
    {
        b.ToTable("franchise_settings");
        b.HasKey(x => x.Id);
    }
}
