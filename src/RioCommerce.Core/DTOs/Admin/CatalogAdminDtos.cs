namespace RioCommerce.Core.DTOs.Admin;

public record CategoryAdminItem(
    Guid Id, string Name, string Slug, string? Description, int DisplayOrder, bool IsActive, int ProductCount,
    Guid? ParentId = null, string? ParentName = null, int Depth = 0);

public class CategoryEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public Guid? ParentId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnHomePage { get; set; }
    public bool IncludeInTopMenu { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoKeywords { get; set; }
    public string? SeoDescription { get; set; }
}

// A product row in the category editor's Products section.
// DisplayOrder is the per-category order held on the ProductCategory mapping (NOT the product's global order).
public record CategoryProductRow(Guid Id, string Title, string Slug, bool IsFeatured, decimal SellingPrice, int DisplayOrder);

// One editable per-category display-order value posted back from the category Products grid.
public record CategoryProductOrder(Guid ProductId, int DisplayOrder);

public record FacultyAdminItem(
    Guid Id, string DisplayName, string ShortCode, string? Designation, string? Qualifications,
    string? ShortDescription,
    int CourseCount, int DisplayOrder, bool IsActive);

public class FacultyEditModel
{
    public Guid? Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Qualifications { get; set; }
    /// <summary>One-liner shown on the Course “About the Faculty” card. Stored unlimited; UI hints at ~300 chars.</summary>
    public string? ShortDescription { get; set; }
    public string? Bio { get; set; }
    public string? PhotoUrl { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? WhatsappNumber { get; set; }
    public string? CallNumber { get; set; }
    public string? SubjectsCsv { get; set; }   // comma-separated for the form
    // ── Faculty profile KPIs (free text — "8+", "25K+", "98%") ──
    public string? YearsOfExperience { get; set; }
    public string? StudentsTaught { get; set; }
    public string? HoursOfTeaching { get; set; }
    public string? StudentSatisfaction { get; set; }
    public string? AirHoldersNote { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnHomePage { get; set; }
}
