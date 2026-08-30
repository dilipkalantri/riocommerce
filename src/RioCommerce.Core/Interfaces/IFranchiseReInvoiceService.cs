using RioCommerce.Core.DTOs.Reporting;

namespace RioCommerce.Core.Interfaces;

/// <summary>An invoice that could have a franchisee re-invoice raised against it, as the picker
/// shows it.</summary>
public sealed record ReInvoiceCandidate(
    Guid InvoiceId, string InvoiceNumber, DateOnly InvoiceDate, string OrderNumber,
    string FranchiseName, Guid FranchiseId, decimal TotalAmount, decimal FranchiseShareAmount,
    bool AlreadyReInvoiced, string? ExistingReInvoiceNumber);

/// <summary>A raised re-invoice, for the detail view.</summary>
public sealed record ReInvoiceDetail(
    Guid Id, string ReInvoiceNumber, DateOnly ReInvoiceDate,
    string OriginalInvoiceNumber, DateOnly OriginalInvoiceDate, string OrderNumber,
    string FranchiseName, string? FranchiseGstin, string? PlaceOfSupply,
    decimal TaxableAmount, decimal Cgst, decimal Sgst, decimal Igst, decimal TotalGst,
    decimal TotalAmount, decimal FranchiseShareAmount, string Status, string? Notes,
    List<ReInvoiceLine> Lines);

public sealed record ReInvoiceLine(
    string ProductTitle, string? SubjectName, int Quantity, decimal UnitPrice,
    decimal TaxableAmount, decimal GstRate, decimal GstAmount, decimal LineTotal);

/// <summary>
/// Raises and cancels franchisee re-invoices (§13).
///
/// <para>Issuing is manual: an admin picks an invoice already issued to a customer and raises the
/// corresponding re-invoice on the franchisee. The tax split is derived with the same rules the
/// customer invoice used, so the two documents reconcile.</para>
/// </summary>
public interface IFranchiseReInvoiceService
{
    /// <summary>Invoices on franchise orders in the window, flagged with whether a live re-invoice
    /// already exists for each.</summary>
    Task<List<ReInvoiceCandidate>> ListCandidatesAsync(
        DateTime? from, DateTime? to, Guid? franchiseId, bool onlyPending, CancellationToken ct = default);

    Task<ReInvoiceDetail?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Raises a re-invoice against an original. Returns the new document, or an error
    /// when the original does not qualify or already has a live re-invoice.</summary>
    Task<(ReInvoiceDetail? doc, string? error)> IssueAsync(
        Guid originalInvoiceId, DateOnly? date, string? notes,
        Guid? actorId, string? actorName, CancellationToken ct = default);

    /// <summary>Cancels a re-invoice. The original becomes eligible for a fresh one — that is the
    /// whole correction path, since an issued tax document is never edited in place.</summary>
    Task<(bool ok, string? error)> CancelAsync(Guid id, string reason, CancellationToken ct = default);
}
