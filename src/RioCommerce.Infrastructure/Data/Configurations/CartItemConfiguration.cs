using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

// Minimal: only sets precision on the new attribute-adjustment column. CartItem otherwise keeps its
// convention-based mapping (table "CartItems", FK cascade/set-null) so the migration stays additive.
public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
        => b.Property(c => c.AttributePriceAdjustment).HasPrecision(10, 2);
}
