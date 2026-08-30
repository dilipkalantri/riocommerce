using RioCommerce.Core.Entities;
using RioCommerce.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> b)
    {
        b.ToTable("message_templates"); b.HasKey(t => t.Id);
        b.Property(t => t.Key).HasMaxLength(60).IsRequired();
        b.HasIndex(t => t.Key).IsUnique();
        b.Property(t => t.Name).HasMaxLength(160).IsRequired();
        b.Property(t => t.Channel).HasMaxLength(20);
        b.Property(t => t.Subject).HasMaxLength(300);
        b.Property(t => t.Body).HasMaxLength(8000);
        b.Property(t => t.CcEmails).HasMaxLength(CcRecipients.MaxStoredLength);
    }
}

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> b)
    {
        b.ToTable("notification_logs"); b.HasKey(l => l.Id);
        b.Property(l => l.TemplateKey).HasMaxLength(60);
        b.Property(l => l.Channel).HasMaxLength(20);
        b.Property(l => l.Recipient).HasMaxLength(200);
        b.Property(l => l.Subject).HasMaxLength(300);
        b.Property(l => l.Body).HasMaxLength(8000);
        b.Property(l => l.Status).HasMaxLength(20);
        b.Property(l => l.Error).HasMaxLength(500);
        b.HasIndex(l => l.CreatedAt);
    }
}

public class UrlRedirectConfiguration : IEntityTypeConfiguration<UrlRedirect>
{
    public void Configure(EntityTypeBuilder<UrlRedirect> b)
    {
        b.ToTable("url_redirects"); b.HasKey(r => r.Id);
        b.Property(r => r.OldUrl).HasMaxLength(500).IsRequired();
        b.Property(r => r.NewUrl).HasMaxLength(500).IsRequired();
        b.HasIndex(r => r.OldUrl).IsUnique();
    }
}
