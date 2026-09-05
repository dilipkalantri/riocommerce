namespace RioCommerce.Core.DTOs.School;

/// <summary>One school-enrolment order in the principal's payment history.</summary>
public record SchoolEnrollmentOrderRow(
    Guid OrderId,
    string OrderNumber,
    DateTime CreatedAt,
    string CourseTitle,
    int StudentCount,
    decimal TotalAmount,
    string PaymentStatus,
    string? PaymentReference,
    int InvoiceCount);

/// <summary>
/// One student's payment line in the School portal's payment history — a roster row joined to the
/// order that pays for it and, when raised, the invoice.
///
/// <para><see cref="InvoiceId"/> is null unless the payment actually succeeded AND an invoice was
/// raised, so the UI can gate the View/Download actions on data rather than on a status string.</para>
/// </summary>
public record SchoolStudentPaymentRow(
    Guid StudentUserId,
    string StudentName,
    string? StudentPhone,
    string CourseTitle,
    decimal Amount,
    DateTime PaymentDate,
    string PaymentStatus,
    bool IsPaid,
    Guid? InvoiceId,
    string? InvoiceNumber,
    string OrderNumber);

/// <summary>
/// Where one student stands on SCHOOL-PAID enrolment, derived entirely from persisted orders.
///
/// <para>"Paid" here means the school actually paid for this pupil — a successful order carrying a
/// SchoolEnrollmentStudents row for them. It is deliberately NOT <c>users.SchoolId != null</c>:
/// a self-registering student picks a school for education details, which creates no obligation on
/// the school and must never put them in the school's payable list.</para>
/// </summary>
public record SchoolStudentPaymentStatus(
    Guid StudentUserId,
    /// <summary>"Paid" | "Pending" | "Failed".</summary>
    string Status,
    bool IsPaid,
    bool IsFailed,
    Guid? InvoiceId,
    string? InvoiceNumber,
    decimal? Amount,
    string? CourseTitle,
    string? OrderNumber);

/// <summary>Headline figures for the School dashboard, all derived from persisted orders.</summary>
public record SchoolPaymentSummary(
    int TotalStudents,
    int PaidStudents,
    int PendingStudents,
    decimal TotalPaid);

/// <summary>One student's tax invoice, as shown in the School portal.</summary>
public record SchoolStudentInvoiceRow(
    Guid InvoiceId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string StudentName,
    string CourseTitle,
    decimal TotalAmount,
    string OrderNumber,
    string PaymentStatus);
