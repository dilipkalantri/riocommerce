using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class BannerSliderSettingsConfiguration : IEntityTypeConfiguration<BannerSliderSettings>
{
    public void Configure(EntityTypeBuilder<BannerSliderSettings> b)
    {
        b.ToTable("banner_slider_settings");
        b.HasKey(x => x.Id);
    }
}
