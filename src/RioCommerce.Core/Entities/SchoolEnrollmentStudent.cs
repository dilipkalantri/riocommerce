namespace RioCommerce.Core.Entities;

/// <summary>
/// One student covered by one school-enrolment order. Backed by 0048_school_enrollment_payment.sql.
///
/// <para>Written when the (unpaid) order is created, so the roster is queryable, authorisable and
/// invoiceable — it used to exist only as free text inside an OrderNote. This is the authority for
/// what to confirm and what to invoice once the payment verifies.</para>
///
/// <para>It is NOT a course-access grant: the existing <see cref="Enrollment"/> table keeps that
/// job, and rows are added there only after server-side payment verification.</para>
/// </summary>
public class SchoolEnrollmentStudent : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid SchoolId { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>Price for THIS student, snapshotted when the order was placed.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Set when the order's payment verified. Null = still pending payment.</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>The invoice raised for this student, once payment verified.</summary>
    public Guid? InvoiceId { get; set; }

    public Order Order { get; set; } = null!;
    public School School { get; set; } = null!;
    public User StudentUser { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
