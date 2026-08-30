using RioCommerce.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace RioCommerce.Infrastructure.Data.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("shipments"); b.HasKey(s => s.Id);
        b.Property(s => s.Courier).HasMaxLength(80);
        b.Property(s => s.TrackingNumber).HasMaxLength(80);
        // Status became a ShipmentStatus enum when the shipping report needed to filter on it.
        // Stored as its NAME, not an ordinal: the column is a pre-existing varchar(20) already
        // holding "Pending"/"Dispatched"/"Delivered", so the enum names round-trip the live data
        // untouched. Casting the column to integer would gain nothing and risk the history.
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Notes).HasMaxLength(500);
        b.HasIndex(s => s.OrderId).IsUnique();
        b.HasIndex(s => s.Status);
        b.HasIndex(s => s.DispatchedAt);
        b.HasOne(s => s.Order).WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> b)
    {
        b.ToTable("return_requests"); b.HasKey(r => r.Id);
        b.Property(r => r.OrderNumber).HasMaxLength(20);
        b.Property(r => r.Reason).HasMaxLength(500);
        b.Property(r => r.Status).HasMaxLength(20);
        b.Property(r => r.RefundAmount).HasPrecision(12, 2);
        b.HasOne(r => r.Order).WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
