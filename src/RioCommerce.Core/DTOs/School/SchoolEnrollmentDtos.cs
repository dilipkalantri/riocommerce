namespace RioCommerce.Core.DTOs.School;

/// <summary>
/// One selectable course in the School Portal enrolment picker.
///
/// Projected straight from the Products table — title and price are whatever the catalogue
/// currently holds. Nothing here is authored in code, so renaming or repricing a product in
/// Admin changes this list with no deployment.
/// </summary>
/// <param name="Price">
/// The EFFECTIVE selling price per student: the special price while its window is open,
/// otherwise the regular selling price. Same rule the storefront and franchise portal use.
/// </param>
public record SchoolEnrollmentProduct(
    Guid Id,
    string Title,
    decimal Price);

/// <summary>
/// A principal's request to enrol a set of their own students on one course.
///
/// NOTE there is deliberately no SchoolId and no price here. The school is resolved server-side
/// from the authenticated principal, and the price is read from the catalogue at submit time, so
/// a crafted request can neither reach another school nor choose what it pays.
/// </summary>
public class PlaceSchoolEnrollmentRequest
{
    public Guid ProductId { get; set; }

    /// <summary>User ids of the students to enrol. Every one is re-checked against the
    /// principal's own school before the order is written.</summary>
    public List<Guid> StudentUserIds { get; set; } = new();
}

/// <summary>
/// Outcome of an enrolment submission.
///
/// <paramref name="OrderNumber"/> is the order the school can quote to Accounts. The order is
/// created UNPAID — <c>Status = Pending</c>, <c>PaymentStatus = Pending</c> — and course access
/// is granted later by the existing admin activation path, not here.
/// </summary>
public record PlaceSchoolEnrollmentResult(
    bool Ok,
    string? Error,
    string? OrderNumber,
    int StudentCount,
    decimal Total);
