namespace RioCommerce.Core.Entities;
public class Testimonial : BaseEntity
{
    public string StudentName { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? ScoreText { get; set; }
    public string? TextContent { get; set; }
    public int? Rating { get; set; }
    public Guid? ProductId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Product? Product { get; set; }
}
