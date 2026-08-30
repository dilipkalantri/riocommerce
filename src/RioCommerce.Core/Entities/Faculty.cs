namespace RioCommerce.Core.Entities;
public class Faculty : BaseEntity
{
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Qualifications { get; set; }
    public string[]? Subjects { get; set; }
    // ── ShortDescription is the one-liner shown on the Product / Course “About the Faculty”
    // card. Stored unlimited; UI hints at ~300 chars. When empty, callers fall back to the
    // first ~250 chars of Bio so existing data still renders something useful. ──
    public string? ShortDescription { get; set; }
    public string? Bio { get; set; }
    public string? PhotoUrl { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? WhatsappNumber { get; set; }
    public string? CallNumber { get; set; }                  // public-facing phone shown on the faculty profile
    // ── Faculty profile page KPIs (free-text so admin can display "25K+", "8+", "98%" etc.) ──
    public string? YearsOfExperience { get; set; }           // e.g. "8+"
    public string? StudentsTaught { get; set; }              // e.g. "25K+"
    public string? HoursOfTeaching { get; set; }             // e.g. "15K+"
    public string? StudentSatisfaction { get; set; }         // e.g. "98%"
    public string? AirHoldersNote { get; set; }              // e.g. "Produced Every Year"
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? BankIfsc { get; set; }
    public string? PanNumber { get; set; }
    public string? Gstin { get; set; }
    public bool GstRegistered { get; set; }

    /// <summary>
    /// Whether this faculty's revenue share attracts GST. THE single definition of the test — the
    /// calculator, the earned-share ledger and the payout run all read this property, so none of
    /// them can disagree about what a faculty is owed.
    ///
    /// <para>Deliberately broader than the franchise equivalent
    /// (<c>!string.IsNullOrWhiteSpace(franchise.Gstin)</c>): Faculty carries BOTH a GSTIN and an
    /// explicit <see cref="GstRegistered"/> flag, and an admin who ticks the flag has stated the
    /// intent even if the number hasn't been captured yet. Treating a ticked flag as unregistered
    /// would silently underpay them. Franchise has no such flag, which is why its test is narrower
    /// rather than inconsistent.</para>
    /// </summary>
    public bool IsGstRegisteredForShare => GstRegistered || !string.IsNullOrWhiteSpace(Gstin);
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnHomePage { get; set; }   // surfaces this faculty in the homepage "Our Core Team" carousel
    public User? User { get; set; }
    public ICollection<Product> PrimaryProducts { get; set; } = new List<Product>();
    public ICollection<FacultySharingRule> SharingRules { get; set; } = new List<FacultySharingRule>();
}
