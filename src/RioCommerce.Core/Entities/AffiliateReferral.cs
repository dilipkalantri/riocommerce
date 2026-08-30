namespace RioCommerce.Core.Entities;
public class AffiliateReferral : BaseEntity
{
    public Guid AffiliateId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal OrderAmount { get; set; }
    public decimal Commission { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public Affiliate Affiliate { get; set; } = null!;
    public Order Order { get; set; } = null!;
}
