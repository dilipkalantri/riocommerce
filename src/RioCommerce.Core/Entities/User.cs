using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class User : BaseEntity
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PhoneCountry { get; set; } = "+91";
    public string? PasswordHash { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; }
    public string? GoogleId { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public CourseLevel? CourseInterest { get; set; }   // doubles as the customer's "Level"
    public string? Gender { get; set; }                // "Male" | "Female" | null

    // ── Student profile (individual registration). All optional: the school flow and
    //    existing accounts never set them. Backed by 0042_student_profile_fields.sql.
    public DateTime? DateOfBirth { get; set; }
    public string? District { get; set; }
    public string? SchoolName { get; set; }
    public string? StudentClass { get; set; }         // "5", "8", "10" …
    public string? Board { get; set; }                // CBSE | ICSE | State Board | Other

    // ── Authoritative master links (0047_student_school_link_and_boards.sql).
    //    SchoolName above is display text and is NOT unique — the imported data has
    //    several schools sharing a name in one district — so SchoolId is the real
    //    relationship. Both stay nullable: existing accounts, school-flow accounts and
    //    anything whose old text could not be resolved unambiguously keep NULL rather
    //    than a guessed FK.
    public Guid? SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>
    /// Taluka the student selected (0049). For a student who picked a school from the master this
    /// duplicates Schools.TalukaId, but it is the ONLY record of the taluka for a student who chose
    /// "Other" and typed their school name — there is no SchoolId to derive it from.
    /// </summary>
    public Guid? TalukaId { get; set; }

    /// <summary>FK to the Boards master. The <see cref="Board"/> string above is kept as the
    /// historical display value; there is deliberately no navigation property, because it
    /// would collide with that property name.</summary>
    public Guid? BoardId { get; set; }

    public string? Attempt { get; set; }               // target exam attempt, e.g. "Sept 2026"
    public string? AdminComment { get; set; }          // internal admin note
    public string? ReferralCode { get; set; }
    public Guid? ReferredById { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
    public User? ReferredBy { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
