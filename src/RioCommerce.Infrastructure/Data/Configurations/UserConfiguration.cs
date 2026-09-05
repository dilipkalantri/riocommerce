using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users"); b.HasKey(u => u.Id);
        b.Property(u => u.Email).HasColumnType("citext");
        b.Property(u => u.Phone).HasMaxLength(15);
        b.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        b.HasIndex(u => u.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
        b.HasIndex(u => u.Phone).IsUnique().HasFilter("\"Phone\" IS NOT NULL");
        b.HasOne(u => u.ReferredBy).WithMany().HasForeignKey(u => u.ReferredById);

        // Master links added by 0047. SetNull, never Cascade: retiring a school or a board
        // must not delete the students attached to it.
        b.HasOne(u => u.School).WithMany()
         .HasForeignKey(u => u.SchoolId).OnDelete(DeleteBehavior.SetNull);
        // No navigation property — User.Board is already the historical text value.
        b.HasOne<Board>().WithMany()
         .HasForeignKey(u => u.BoardId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Taluka>().WithMany()
         .HasForeignKey(u => u.TalukaId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(u => u.SchoolId);
        b.HasIndex(u => u.BoardId);
        b.HasIndex(u => u.TalukaId);
    }
}
