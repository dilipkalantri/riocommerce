using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Lead : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? CourseInterest { get; set; }
    public string? Message { get; set; }
    /// <summary>Free-text capture tag, e.g. "website_popup". Unchanged — still written by every
    /// capture path and shown on the detail page. <see cref="LeadSource"/> is the typed version
    /// the admin filters on.</summary>
    public string? Source { get; set; }

    /// <summary>Typed origin, used for the admin's Home / Blog / Contact separation.</summary>
    public LeadSource LeadSource { get; set; } = LeadSource.Other;

    /// <summary>The blog whose counselling form produced this lead. Null for every non-blog lead.
    /// A reference rather than a copied title, so a renamed post stays correct everywhere.</summary>
    public Guid? BlogPostId { get; set; }
    public BlogPost? BlogPost { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.New;
    public Guid? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }

    /// <summary>When the admin plans to contact this lead next. Null = nothing scheduled.
    /// Set only from the admin CRM view — website lead capture never populates it.</summary>
    public DateTime? NextFollowUpAt { get; set; }

    /// <summary>Append-only follow-up/conversation history. See <see cref="LeadNote"/>.</summary>
    public ICollection<LeadNote> Notes { get; set; } = new List<LeadNote>();
}
