using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Orders;

public class OrderListItem
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Originating system's reference for the order (§4, §31). Null when none was captured
    /// — most website orders, and everything created before the field existed.</summary>
    public string? SourceNo { get; set; }

    public DateTime CreatedAt { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    /// <summary>Compact one-line summary for the grid: first product plus "+N more".</summary>
    public string ProductSummary { get; set; } = string.Empty;

    /// <summary>Every product on the order with the lecture mode and purchase options that were
    /// chosen, "(xN)" appended when the quantity is above one. Exports use this —
    /// <see cref="ProductSummary"/> deliberately hides the rest to fit a table cell, which is wrong
    /// in a spreadsheet.</summary>
    public List<string> ProductNames { get; set; } = new();

    /// <summary>Billing address as one line (address, city, state, PIN), blanks skipped.</summary>
    public string BillingAddressFull { get; set; } = string.Empty;
    /// <summary>Shipping address as one line. Empty when the order ships to the billing address.</summary>
    public string ShippingAddressFull { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? FacultyName { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderSource Source { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public EnrollmentStatus Enrollment { get; set; }
    public string? FranchiseName { get; set; }
    public decimal FranchiseCommission { get; set; }
    /// <summary>Snapshot of the "Referred By" option name captured at checkout. Null when skipped.</summary>
    public string? ReferredByName { get; set; }
    public ReferralSourceType? ReferralType { get; set; }

    // ── 💰 Finance/ERP grid additions (additive — computed in projection, no DB columns) ──
    // Per-row financial breakdown shown on the redesigned admin order list. Derived from existing
    // Order/OrderItem columns so there are no migrations and no API breaking changes; everything is
    // sourced from the same database snapshot and obeys the spec formulas to the rupee.
    /// <summary>Original product price total (list), before any discount. Sum of UnitPrice × Quantity —
    /// UnitPrice is stored as the list price, so the discount is not added back.</summary>
    public decimal GrossAmount { get; set; }
    /// <summary>Discount given to the student (coupon-level + any per-item discount). Always ≥ 0.</summary>
    public decimal StudentDiscount { get; set; }
    /// <summary>Franchise discount percentage (0 for non-franchise orders). Two decimal places.</summary>
    public decimal FranchiseDiscountPercent { get; set; }
    /// <summary>Franchise discount amount in rupees (0 for non-franchise orders).</summary>
    public decimal FranchiseDiscountAmount { get; set; }
    /// <summary>Final paid amount (GST inclusive) — same as <see cref="TotalAmount"/>; aliased for clarity.</summary>
    public decimal NetAmount { get; set; }
    /// <summary>Taxable portion of <see cref="NetAmount"/> — Net × 100 / 118 (spec formula) when GST is 18%.</summary>
    public decimal TaxableAmount { get; set; }
    /// <summary>Extracted GST portion — Net × 18 / 118 (spec formula).</summary>
    public decimal GstAmount { get; set; }
    /// <summary>Friendly course-level label for the Product cell subtitle (e.g. "Beginner").</summary>
    public string? CourseLevel { get; set; }
    /// <summary>True when an affiliate is attributed to the order (drives the "Affiliate" source badge).</summary>
    public bool HasAffiliate { get; set; }
    /// <summary>True when the order is an organisation/B2B sale (drives the "Corporate" source badge).</summary>
    public bool IsCorporate { get; set; }
}

/// <summary>Aggregate financial totals for the rows matching the current filter — drives the summary bar.</summary>
public record OrderFinanceSummary(
    int TotalOrders,
    decimal GrossRevenue,
    decimal StudentDiscount,
    decimal FranchiseDiscount,
    decimal NetRevenue,
    decimal GstTotal,
    decimal TaxableTotal);

// Lookups for the order filter dropdowns.
public record OrderFilterMeta(List<IdName> Faculty, List<IdName> Franchises);

public class OrderItemLine
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public string? ModeName { get; set; }
    public string? Sku { get; set; }
    public string? ImageUrl { get; set; }
    public string? FacultyName { get; set; }
    public string? BatchType { get; set; }
    public string? Duration { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>Franchise-order display only: this line's franchisee share, shown as a per-line
    /// discount so Unit − Share = net line total. Zero for non-franchise orders.</summary>
    public decimal FranchiseShare { get; set; }
}

public class UpdateOrderItemRequest
{
    public Guid ItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class AddOrderItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ModeId { get; set; }
    public int Quantity { get; set; } = 1;
}

public record ProductPickMode(Guid Id, string Name, decimal Price);
public record ProductPickItem(
    Guid Id, string Title, string? Sku, decimal SellingPrice, string? FacultyName, string? ImageUrl, List<ProductPickMode> Modes);

public class OrderDetail
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? UserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    public string? StudentCity { get; set; }
    public OrderSource Source { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    /// <summary>Gateway-reported instrument the customer actually paid with — "UPI", "Credit Card",
    /// "Net Banking", … Null for offline orders and unreported online payments.</summary>
    public string? GatewayPaymentMode { get; set; }
    public EnrollmentStatus Enrollment { get; set; }
    public string? CouponCode { get; set; }
    public string? InternalNotes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    // Billing
    public string? BillingName { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }
    public string? GstNumber { get; set; }
    public string? GstClassification { get; set; }
    // Shipping — separate from billing only when the goods go somewhere else. Null means "same as
    // billing", which is what the dispatch screen and the courier label fall back to.
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPincode { get; set; }
    /// <summary>True once a tax invoice has been issued. The billing STATE is frozen from that point
    /// — it decides CGST+SGST vs IGST, and the issued invoice already states one of them.</summary>
    public bool HasInvoice { get; set; }

    // ── Dispatch / shipment ──
    // Read from the Shipments row whose OrderId IS this order (unique index: at most one). Never
    // resolved by customer, phone, email or product, so a detail page cannot surface someone
    // else's consignment. The dispatch screen remains the only place these values are edited.

    /// <summary>Whether a dispatch record exists at all. Distinct from a Pending status: an order
    /// nobody has touched yet and one deliberately parked at Pending both read "Pending", but only
    /// the second has a courier the admin may already have chosen.</summary>
    public bool HasShipment { get; set; }

    /// <summary>Status of THIS order's consignment. Defaults to Pending when no record exists,
    /// matching what the dispatch board shows for the same order.</summary>
    public ShipmentStatus ShipmentStatus { get; set; } = ShipmentStatus.Pending;

    /// <summary>Courier name exactly as stored — resolve display/tracking through
    /// <c>Core.Shipping.Couriers</c>, never by comparing this string in a view.</summary>
    public string? ShipmentCourier { get; set; }

    /// <summary>AWB / consignment number. Null for a courier that does not track (PCMC).</summary>
    public string? ShipmentTrackingNumber { get; set; }

    public DateTime? DispatchedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    // Franchise / faculty
    public string? FranchiseName { get; set; }
    public decimal FranchiseCommission { get; set; }
    public decimal FacultyCommission { get; set; }

    // ── Franchise-order display block (Source == Franchisee only) ──
    // The franchisee's share is treated as a discount: the order totals card shows
    // Subtotal → Franchisee Discount → Net Amount, with tax recomputed on the NET amount
    // (not the gross). All zero / IsFranchiseView=false for normal orders, which keep using
    // the existing Subtotal/GstAmount/TotalAmount fields unchanged.
    public bool IsFranchiseView { get; set; }
    public decimal FranchiseDiscountTotal { get; set; }   // Σ per-line franchisee share
    public decimal FranchiseNetAmount { get; set; }        // Subtotal − discount (GST-inclusive net)
    public decimal FranchiseGst { get; set; }              // GST contained in the net
    public decimal FranchiseCgst { get; set; }
    public decimal FranchiseSgst { get; set; }
    public decimal FranchiseIgst { get; set; }
    public decimal FranchiseTaxable { get; set; }          // net − GST
    // Referred By (snapshot taken at checkout — never overwritten by admin edits to the source list)
    public string? ReferredByName { get; set; }
    public ReferralSourceType? ReferralType { get; set; }
    public string? ReferralCustomText { get; set; }
    /// <summary>The staff member who raised this order — the counsellor, for a counter order.
    /// Null when the customer placed it themselves on the storefront, and for orders created before
    /// this was captured.</summary>
    public string? CreatedByName { get; set; }
    // Lifecycle
    public bool IsDeleted { get; set; }
    public string? InvoiceNumber { get; set; }
    public List<OrderItemLine> Items { get; set; } = new();
    public List<OrderPaymentLine> Payments { get; set; } = new();
}

public record OrderPaymentLine(decimal Amount, PaymentMode Mode, PaymentStatus Status, string? Reference, DateTime? PaidAt,
    string? GatewayMode = null);

public record OrderNoteItem(Guid Id, string Body, bool IsCustomerVisible, bool IsPinned, string CreatedByName, DateTime CreatedAt);

public class AddOrderNoteRequest
{
    public string Body { get; set; } = string.Empty;
    public bool IsCustomerVisible { get; set; }
    public bool IsPinned { get; set; }
}

// Everything the printable invoice page needs.
public class InvoiceView
{
    /// <summary>Franchise order funded from the wallet — no tax invoice exists by design, because
    /// the wallet top-up that paid for it was invoiced instead. The screen shows an explanation
    /// rather than a "DRAFT (unpaid)" placeholder, which would read as a fault.</summary>
    public bool PaidFromWallet { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }
    public string? GstNumber { get; set; }
    public string GstClassification { get; set; } = "B2C";
    public string? FranchiseName { get; set; }

    // ── Franchise-order billing (the franchisee is the billed party; the student is a reference) ──
    public bool IsFranchiseOrder { get; set; }
    public string? FranchiseGstin { get; set; }
    public string? FranchiseAddress { get; set; }
    public string? FranchiseCity { get; set; }
    public string? FranchiseState { get; set; }
    public string? FranchisePincode { get; set; }
    public decimal FranchiseShareAmount { get; set; }   // commission deducted from gross
    public decimal CompanyShareAmount { get; set; }     // gross − franchise share
    public decimal NetPayable { get; set; }             // what the franchisee pays
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    /// <summary>Gateway-reported instrument printed on the invoice — "UPI", "Credit Card", …</summary>
    public string? GatewayPaymentMode { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<OrderItemLine> Items { get; set; } = new();
}

public record OrderStats(int Total, int Confirmed, int Pending, decimal Revenue);

public class CreateOrderRequest
{
    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    public string? StudentCity { get; set; }
    public Guid ProductId { get; set; }
    public string? ModeName { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public PaymentMode PaymentMode { get; set; } = PaymentMode.Cash;
    public OrderSource Source { get; set; } = OrderSource.Counter;
    public string? InternalNotes { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Confirmed;
}

// ── V2 admin "Create Order" wizard ──────────────────────────────────────────────
// Multi-item, per-line product attribute selections, billing, split payments,
// and a mandatory referral source. Drives /admin/orders/create.

public class CreateOrderRequestV2
{
    public Guid? UserId { get; set; }

    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }

    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string BillingState { get; set; } = "Maharashtra";
    public string? BillingPincode { get; set; }
    public string? GstNumber { get; set; }

    /// <summary>Optional separate shipping address. When null the billing address is used.</summary>
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPincode { get; set; }

    public List<CreateOrderLineV2> Items { get; set; } = new();
    public List<CreateOrderPaymentV2> Payments { get; set; } = new();

    /// <summary>
    /// Optional installment plan. When <c>Enabled</c>, the order is created as a partial-payment
    /// order: only the down payment is collected up front, PaymentStatus stays Pending (no final
    /// invoice yet), and the schedule is created. The <c>Payments</c> list is ignored for the
    /// total-match check in favour of the down payment.
    /// </summary>
    public RioCommerce.Core.DTOs.Installments.InstallmentPlanInput? Installment { get; set; }

    public OrderSource Source { get; set; } = OrderSource.Counter;

    /// <summary>The originating system's reference for this order (§3, §32) — a CaseHub case ID, a
    /// counter receipt number, a franchisee's own reference. Optional; its meaning follows
    /// <see cref="Source"/>.</summary>
    public string? SourceNo { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Confirmed;

    public Guid? ReferralSourceId { get; set; }
    public string? ReferralCustomText { get; set; }

    public string? InternalNotes { get; set; }

    // ── Franchise context (optional) ────────────────────────────────────────
    // Set by the Create Franchisee Order wizard. When provided, the service tags
    // the order with FranchiseId + Source=Franchisee, debits the franchise wallet
    // when Mode=Wallet (credit-limit aware), and records the commission entry
    // after the order saves successfully.

    /// <summary>The franchise this order is being placed under. Null = regular admin order.</summary>
    public Guid? FranchiseId { get; set; }

    /// <summary>Wallet / NetPayment / Postpay. Drives wallet debiting + commission accrual.</summary>
    public FranchiseTxnMode? FranchiseTxnMode { get; set; }
}

public class CreateOrderLineV2
{
    public Guid ProductId { get; set; }
    public Guid? ProductModeId { get; set; }
    public List<Guid> SelectedAttributeValueIds { get; set; } = new();
    public List<Guid> SelectedOptionIds { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public decimal Discount { get; set; }
}

public class CreateOrderPaymentV2
{
    public PaymentMode Mode { get; set; }
    public decimal Amount { get; set; }
}

public record ProductPickAttributeValue(
    Guid Id, string Name,
    decimal PriceAdjustment, bool PriceAdjustmentUsePercentage, bool IsPreSelected);

public record ProductPickAttributeGroup(
    Guid MappingId, string Name, string ControlType, bool IsRequired,
    List<ProductPickAttributeValue> Values);

public record ProductPickDetail(
    Guid Id, string Title, string? Sku, string? FacultyName, string? BatchStatusText,
    string? ImageUrl, decimal SellingPrice, decimal Mrp, decimal GstRate,
    List<ProductPickMode> Modes,
    List<ProductPickAttributeGroup> Attributes);

/// <summary>
/// Per-order billing and shipping address, edited from the order page.
///
/// <para>Addresses drift for ordinary reasons — a typo, a pincode the courier rejects, goods that
/// need to go to a different address than the bill. This carries only the address; nothing about
/// money, items or status travels with it.</para>
/// </summary>
public class OrderAddressEdit
{
    public Guid OrderId { get; set; }

    public string? BillingName { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    /// <summary>Decides CGST+SGST (intra-state) vs IGST (inter-state). Changing it re-splits the
    /// order's tax, and is refused outright once a tax invoice has been issued.</summary>
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }

    /// <summary>Leave every shipping field blank to ship to the billing address.</summary>
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPincode { get; set; }
}
