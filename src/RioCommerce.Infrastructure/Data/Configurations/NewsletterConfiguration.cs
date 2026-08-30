using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class NewsletterSubscriberConfiguration : IEntityTypeConfiguration<NewsletterSubscriber>
{
    public void Configure(EntityTypeBuilder<NewsletterSubscriber> b)
    {
        b.ToTable("newsletter_subscribers"); b.HasKey(s => s.Id);
        b.Property(s => s.Email).HasMaxLength(200).IsRequired();
        b.HasIndex(s => s.Email).IsUnique();
        b.Property(s => s.Name).HasMaxLength(200);
        b.Property(s => s.Source).HasMaxLength(60);
    }
}

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.ToTable("campaigns"); b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(160).IsRequired();
        b.Property(c => c.Subject).HasMaxLength(200).IsRequired();
        b.Property(c => c.Body).HasMaxLength(8000);
    }
}
