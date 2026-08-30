using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class FacultyConfiguration : IEntityTypeConfiguration<Faculty>
{
    public void Configure(EntityTypeBuilder<Faculty> b)
    {
        b.ToTable("faculty"); b.HasKey(f => f.Id);
        b.Property(f => f.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(f => f.ShortCode).HasMaxLength(10).IsRequired();
        b.HasIndex(f => f.ShortCode).IsUnique();
        b.HasOne(f => f.User).WithOne().HasForeignKey<Faculty>(f => f.UserId);
    }
}
