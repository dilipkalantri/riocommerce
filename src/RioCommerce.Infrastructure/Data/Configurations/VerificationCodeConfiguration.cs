using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RioCommerce.Infrastructure.Data.Configurations;

public class VerificationCodeConfiguration : IEntityTypeConfiguration<VerificationCode>
{
    public void Configure(EntityTypeBuilder<VerificationCode> b)
    {
        b.ToTable("verification_codes");
        b.HasKey(x => x.Id);

        b.Property(x => x.Target).HasMaxLength(256).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(256).IsRequired();

        // Purpose/Channel persist as ints (not native pg enums) — keeps them independent of the
        // Npgsql enum-mapping setup and avoids a migration for every new value.
        b.Property(x => x.Purpose).HasConversion<int>();
        b.Property(x => x.Channel).HasConversion<int>();

        b.Ignore(x => x.IsConsumed);

        b.HasIndex(x => new { x.Purpose, x.Target });
    }
}
