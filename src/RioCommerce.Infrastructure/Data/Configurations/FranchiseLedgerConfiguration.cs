using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;
public class FranchiseLedgerConfiguration : IEntityTypeConfiguration<FranchiseLedgerEntry>
{
    public void Configure(EntityTypeBuilder<FranchiseLedgerEntry> b)
    {
        b.ToTable("franchise_ledger"); b.HasKey(e => e.Id);
        b.Property(e => e.Amount).HasPrecision(12, 2);
        b.Property(e => e.BalanceAfter).HasPrecision(12, 2);
        b.Property(e => e.Description).HasMaxLength(300);
        b.HasIndex(e => e.FranchiseId);
        b.HasOne(e => e.Franchise).WithMany().HasForeignKey(e => e.FranchiseId).OnDelete(DeleteBehavior.Cascade);
    }
}
