using RioCommerce.Core.DTOs.Invoices;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Invoice generation, lookup and PDF rendering.
///
/// Generation is <b>idempotent</b> — calling <see cref="EnsureForOrderAsync"/> on an order
/// that already has a live invoice returns the existing one. Safe to call from every
/// payment-success hook.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Generates an invoice for the order if it's paid AND doesn't already have a live
    /// one. Returns <c>null</c> when the order isn't paid yet, isn't found, or already had one
    /// (in which case <paramref name="existingId"/> is set to the existing invoice).</summary>
    Task<(Guid? newId, Guid? existingId, string? error)> EnsureForOrderAsync(Guid orderId, Guid? actorId = null, string? actorName = null, CancellationToken ct = default);

    /// <summary>Idempotent batch trigger — used by background jobs / admin "regenerate all" actions.</summary>
    Task<int> EnsureForPaidOrdersAsync(DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Cancels (voids) a live invoice. Reason is recorded; future generation for the
    /// same order produces a new invoice number.</summary>
    Task<bool> CancelAsync(Guid invoiceId, string reason, Guid? actorId, string? actorName, CancellationToken ct = default);

    Task<List<InvoiceListItem>> ListAsync(InvoiceListFilter filter, CancellationToken ct = default);
    Task<InvoiceDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDetail?> GetByOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Renders a PDF for the given invoice. Returns raw bytes; the controller wraps
    /// them in a FileResult with the right Content-Disposition.</summary>
    Task<(byte[] bytes, string filename)?> RenderPdfAsync(Guid invoiceId, CancellationToken ct = default);
}
