using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class SerialKeyRecordConfiguration : IEntityTypeConfiguration<SerialKeyRecord>
{
    public void Configure(EntityTypeBuilder<SerialKeyRecord> b)
    {
        b.ToTable("serial_key_records");
        b.HasKey(x => x.Id);

        b.Property(x => x.ProviderKey).HasMaxLength(40).IsRequired();
        b.Property(x => x.TenantRef).HasMaxLength(64);
        b.Property(x => x.SerialKey).HasMaxLength(256);
        b.Property(x => x.ExternalReference).HasMaxLength(128);
        b.Property(x => x.ErrorCode).HasMaxLength(64);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.RevokedReason).HasMaxLength(500);

        // Registration-style outcome (Superclass); null for key-issuing providers.
        b.Property(x => x.SubscriptionStatus).HasMaxLength(64);
        b.Property(x => x.ProviderCourseRef).HasMaxLength(64);

        // Bodies are jsonb so we can grep / index into specific JSON fields if needed for support.
        b.Property(x => x.RequestPayload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ApiRequestPayload).HasColumnType("jsonb");
        b.Property(x => x.ResponsePayload).HasColumnType("jsonb");

        // Lookup paths: by order (admin order view), by product (reports), by serial key (validate/activate route).
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.OrderItemId);
        b.HasIndex(x => x.ProductId);
        b.HasIndex(x => x.UserId);

        // Filtered unique — only enforce uniqueness once a key actually exists. Pending rows are nullable
        // and may overlap during retries.
        b.HasIndex(x => x.SerialKey)
            .IsUnique()
            .HasFilter("\"SerialKey\" IS NOT NULL");

        // Hot index for the retry-task query: "status in (pending, failed) and NextRetryAt <= now".
        // Composite on (Status, NextRetryAt) gets us both predicates in one seek.
        b.HasIndex(x => new { x.Status, x.NextRetryAt });

        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class RioPlayTenantConfiguration : IEntityTypeConfiguration<RioPlayTenant>
{
    public void Configure(EntityTypeBuilder<RioPlayTenant> b)
    {
        b.ToTable("rioplay_tenants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.BaseUrl).HasMaxLength(300).IsRequired();
        b.Property(x => x.SecretEncrypted).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => x.TenantId).IsUnique();
        // Partial unique index — exactly one row can be flagged default.
        b.HasIndex(x => x.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\" = TRUE");
    }
}

public class SuperclassSettingsConfiguration : IEntityTypeConfiguration<SuperclassSettings>
{
    public void Configure(EntityTypeBuilder<SuperclassSettings> b)
    {
        b.ToTable("superclass_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.ApiBaseUrl).HasMaxLength(300).IsRequired();
        b.Property(x => x.ApiKeyEncrypted).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Environment).HasMaxLength(40).IsRequired();

        // Singleton: at most one active row (mirrors RioPlay's partial-unique IsDefault pattern).
        b.HasIndex(x => x.IsActive)
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
    }
}

public class ProductSerialKeyConfigConfiguration : IEntityTypeConfiguration<ProductSerialKeyConfig>
{
    public void Configure(EntityTypeBuilder<ProductSerialKeyConfig> b)
    {
        b.ToTable("product_serial_key_configs");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProviderKey).HasMaxLength(40).IsRequired();
        b.Property(x => x.ProviderProductCode).HasMaxLength(64);
        b.Property(x => x.ConfigJson).HasColumnType("jsonb").IsRequired();

        b.HasIndex(x => x.ProductId);
        // One active config per (product, mode, provider). ProductModeId is nullable — the product-level
        // row (NULL mode) and each per-mode override coexist. Filtered so inactive rows can stack up.
        b.HasIndex(x => new { x.ProductId, x.ProductModeId, x.ProviderKey, x.IsActive })
            .HasFilter("\"IsActive\" = TRUE");

        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A per-mode override is cleaned up when its mode is removed.
        b.HasOne(x => x.ProductMode).WithMany().HasForeignKey(x => x.ProductModeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ValencePackConfiguration : IEntityTypeConfiguration<ValencePack>
{
    public void Configure(EntityTypeBuilder<ValencePack> b)
    {
        b.ToTable("valence_packs");
        b.HasKey(x => x.Id);

        b.Property(x => x.PackName).HasMaxLength(300).IsRequired();
        b.Property(x => x.Tags).HasMaxLength(1000);

        // ExternalId is Valence's integer pack id — unique so re-syncs upsert cleanly and the
        // product config can store/lookup by it.
        b.HasIndex(x => x.ExternalId).IsUnique();
    }
}
