using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

public class OrderFilter
{
    // Quick search (order #, name, phone, email) — kept for convenience alongside the specific fields.
    public string? Search { get; set; }
    public string? OrderNumber { get; set; }
    public string? StudentName { get; set; }
    public string? StudentEmail { get; set; }
    public string? StudentPhone { get; set; }
    public string? Product { get; set; }            // matches any order item's product title (legacy text filter)
    /// <summary>Strict product-id filter — populated by the searchable Product dropdown on the order list.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Originating system's reference for the order (§3, §4). Also matched by
    /// <see cref="Search"/>, so this is the narrow version for when only that field should match.</summary>
    public string? SourceNo { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? FacultyId { get; set; }
    public Guid? FranchiseId { get; set; }
    public OrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public EnrollmentStatus? Enrollment { get; set; }
    public OrderSource? Source { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public string? CouponCode { get; set; }
    public string? Notes { get; set; }              // matches internal notes
    // ── 💰 Finance/ERP filters (additive — empty by default, server-side applied) ──
    /// <summary>Minimum gross amount (sticker total before any discount).</summary>
    public decimal? GrossMin { get; set; }
    public decimal? GrossMax { get; set; }
    /// <summary>Minimum net amount (final paid, GST inclusive).</summary>
    public decimal? NetMin { get; set; }
    public decimal? NetMax { get; set; }
    /// <summary>true = only orders with a student discount applied; false = only orders without one.</summary>
    public bool? HasStudentDiscount { get; set; }
    public decimal? FranchiseDiscountPctMin { get; set; }
    public decimal? FranchiseDiscountPctMax { get; set; }
    public decimal? TaxMin { get; set; }
    public decimal? TaxMax { get; set; }
    // ── Multi-select filters (§6, §17). Empty list = All, matching ReportQuery's convention.
    //    Additive: the single-value fields above still work, so nothing that already calls this
    //    service has to change. ──
    public List<Guid> FacultyIds { get; set; } = new();
    public List<Guid> ProductIds { get; set; } = new();
    public List<Guid> SubjectIds { get; set; } = new();
    public List<Guid> FranchiseIds { get; set; } = new();
    public List<OrderStatus> Statuses { get; set; } = new();
    public List<PaymentStatus> PaymentStatuses { get; set; } = new();
    public List<PaymentMode> PaymentModes { get; set; } = new();
    public List<OrderSource> Sources { get; set; } = new();

    /// <summary>date | amount | order | gross | net | gst | taxable | student | sourceno</summary>
    public string SortBy { get; set; } = "date";
    public bool SortDesc { get; set; } = true;
    public bool OnlyDeleted { get; set; }            // show the recycle-bin of soft-deleted orders
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

public interface IOrderAdminService
{
    Task<PagedResult<OrderListItem>> ListAsync(OrderFilter filter);
    Task<OrderStats> StatsAsync();
    /// <summary>Live financial aggregate over the filtered records — drives the summary bar.</summary>
    Task<OrderFinanceSummary> SummaryAsync(OrderFilter filter);
    /// <summary>
    /// Dropdown data for the order-list filter panel.
    ///
    /// <para>Pass <paramref name="productId"/> — the product currently picked in the Product filter —
    /// and the Faculty list narrows to whoever teaches it, via <c>ProductFaculty</c> so a co-taught
    /// course lists every one of its teachers. Franchisees are never narrowed: a franchise has no
    /// catalog relationship to cascade through.</para>
    ///
    /// <para>Omit it and the lists are exactly what they have always been.</para>
    /// </summary>
    Task<OrderFilterMeta> GetFilterMetaAsync(Guid? productId = null);
    Task<OrderDetail?> GetDetailAsync(Guid id);

    /// <summary>Updates one order's billing/shipping address. Address only — no items, totals or
    /// status. Re-splits CGST/SGST/IGST when the billing state changes, and refuses a state change
    /// once a tax invoice has been issued.</summary>
    Task<(bool ok, string? error)> UpdateAddressesAsync(OrderAddressEdit req, Guid? actorId, CancellationToken ct = default);
    Task<string> CreateAsync(CreateOrderRequest request);

    /// <summary>
    /// Move an order to a new status.
    ///
    /// <para><b>Cancelling is restricted to super admins.</b> A cancellation reverses a sale the
    /// customer has usually already paid for, so it is gated here rather than only on the button —
    /// the button can be bypassed via the API, this cannot. Every other status is unrestricted.</para>
    /// </summary>
    Task<(bool ok, string? error)> UpdateStatusAsync(Guid id, OrderStatus status, Guid? actorId = null, string? actorName = null);

    /// <summary>Bulk status change. Cancelling is super-admin only, exactly as in
    /// <see cref="UpdateStatusAsync"/> — the whole batch is refused rather than partly applied.</summary>
    Task<(int updated, string? error)> BulkUpdateStatusAsync(IReadOnlyList<Guid> ids, OrderStatus status, Guid? actorId = null, string? actorName = null);
    Task<string> ExportCsvAsync(OrderFilter filter);

    // ── Notes ──
    Task<List<OrderNoteItem>> ListNotesAsync(Guid orderId);
    Task<Guid> AddNoteAsync(Guid orderId, AddOrderNoteRequest req, Guid? actorId, string actorName);
    Task DeleteNoteAsync(Guid noteId, Guid? actorId, string actorName);
    Task ToggleNotePinAsync(Guid noteId);

    // ── Soft delete / restore ──
    /// <summary>Move an order to the recycle bin. <b>Super admins only</b> — same reasoning as
    /// cancelling: it removes a paid sale from every list and report it feeds.</summary>
    Task<(bool ok, string? error)> SoftDeleteAsync(Guid id, Guid? actorId, string actorName);
    Task<bool> RestoreAsync(Guid id, Guid? actorId, string actorName);

    // ── Enrollment activation ──
    Task<int> ActivateEnrollmentAsync(Guid id, Guid? actorId, string actorName);

    /// <summary>
    /// Send the order-confirmation email again, for an order the customer has already PAID for.
    ///
    /// <para>Unpaid orders are refused: the confirmation says the order is confirmed and states an
    /// amount paid, so sending it before payment tells the customer something untrue. That guard
    /// lives here rather than only on the button — the button can be bypassed, this cannot.</para>
    ///
    /// <para>Returns the address it went to so the caller can show the admin exactly who was
    /// mailed, which matters when the order's email was corrected before resending.</para>
    /// </summary>
    Task<(bool ok, string? error, string? sentTo)> ResendOrderEmailAsync(
        Guid id, Guid? actorId, string actorName, CancellationToken ct = default);

    // ── Invoice ──
    Task<InvoiceView?> GetOrCreateInvoiceAsync(Guid id, Guid? actorId, string actorName);

    // ── Per-order audit history ──
    Task<List<AuditLogItem>> GetHistoryAsync(Guid id);

    // ── Order item management ──
    Task<(bool ok, string? error)> UpdateItemAsync(Guid orderId, UpdateOrderItemRequest req, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> AddItemAsync(Guid orderId, AddOrderItemRequest req, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> RemoveItemAsync(Guid orderId, Guid itemId, Guid? actorId, string actorName);
    Task<List<ProductPickItem>> SearchProductsAsync(string? q, int take = 12);

    /// <summary>Loads everything the Create Order wizard needs to render the attribute
    /// cards for a product: modes, mappings, values, default selections.</summary>
    Task<ProductPickDetail?> GetProductPickDetailAsync(Guid productId);

    /// <summary>Multi-item, split-payment, billing-aware admin order creation.
    /// Returns the new order id so the caller can redirect to /admin/orders/{id}.</summary>
    Task<Guid> CreateOrderV2Async(CreateOrderRequestV2 request, Guid? actorId, string? actorName);

    /// <summary>Lightweight published-product list (Id + Title) for the order-list Product filter dropdown.
    /// Active products only, ordered by Title. Honours the optional <paramref name="q"/> search; capped at
    /// <paramref name="take"/> rows so very large catalogues stream incrementally as the user types.</summary>
    /// <summary>
    /// Options for the order list's searchable Product filter.
    ///
    /// <para>Pass <paramref name="facultyId"/> — whoever is picked in the Faculty filter — and the
    /// list narrows to the courses that faculty is mapped to teach. Catalog only: a course stays on
    /// offer whether or not it has sold in the chosen dates, and the order results themselves are
    /// still filtered by every date and status filter the screen applies.</para>
    ///
    /// <para>Omit it and the list is exactly what it has always been.</para>
    /// </summary>
    Task<List<RioCommerce.Core.DTOs.Meta.IdName>> ListProductsForFilterAsync(string? q, int take = 50, Guid? facultyId = null);
}
