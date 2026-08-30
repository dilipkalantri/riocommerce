using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Review : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public int Rating { get; set; }                 // 1..5
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;   // snapshot of the user's name at submit time
    public bool IsVerifiedPurchase { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public Product Product { get; set; } = null!;
    public User User { get; set; } = null!;
}
