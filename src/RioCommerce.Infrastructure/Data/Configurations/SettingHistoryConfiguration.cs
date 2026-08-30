using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class SettingHistoryConfiguration : IEntityTypeConfiguration<SettingHistory>
{
    public void Configure(EntityTypeBuilder<SettingHistory> b)
    {
        b.ToTable("setting_history"); b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.ChangedByName).HasMaxLength(200);
        b.HasIndex(x => x.Key);
        b.HasIndex(x => x.CreatedAt);
    }
}
