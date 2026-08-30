namespace RioCommerce.Core.Entities;

// A timeline note on an order. Internal notes are admin-only; customer-visible notes can be surfaced to the student.
public class OrderNote : BaseEntity
{
    public Guid OrderId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsCustomerVisible { get; set; }   // false = internal/admin only
    public bool IsPinned { get; set; }            // sticky note, shown first
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "system";
    public Order Order { get; set; } = null!;
    public ICollection<NoteAttachment> Attachments { get; set; } = new List<NoteAttachment>();
}
