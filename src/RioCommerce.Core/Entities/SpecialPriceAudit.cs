namespace RioCommerce.Core.Entities;

/// <summary>Audit trail for every Special Price change on a Product.
/// Logged automatically by ProductAdminService.SaveAsync when any special-price field is modified.</summary>
public class SpecialPriceAudit
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Previous special price (null if it was unset).</summary>
    public decimal? OldPrice { get; set; }
    /// <summary>New special price (null if it was cleared).</summary>
    public decimal? NewPrice { get; set; }

    public DateTime? OldStartDate { get; set; }
    public DateTime? NewStartDate { get; set; }
    public DateTime? OldEndDate { get; set; }
    public DateTime? NewEndDate { get; set; }

    public Guid? ModifiedByUserId { get; set; }
    public string ModifiedByName { get; set; } = string.Empty;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string? Remarks { get; set; }

    public Product Product { get; set; } = null!;
}
