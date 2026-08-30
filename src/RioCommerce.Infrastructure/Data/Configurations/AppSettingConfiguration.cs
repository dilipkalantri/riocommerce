using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_settings"); b.HasKey(a => a.Key);
        b.Property(a => a.Key).HasMaxLength(100);
        b.Property(a => a.Value).IsRequired();
    }
}
