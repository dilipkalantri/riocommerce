using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    public string? StudentCity { get; set; }
    public OrderSource Source { get; set; } = OrderSource.Website;

    /// <summary>Reference number for this order in the system it originated from. Its meaning
    /// follows <see cref="Source"/> — a CaseHub case ID for a website order, a counter receipt
    /// number, a franchisee's own reference. Free text because the formats differ per source and
    /// none of them are ours to validate. Indexed: it is a search key on the order list.</summary>
    public string? SourceNo { get; set; }

    public Guid? FranchiseId { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid? AffiliateId { get; set; }   // referral attribution (set at checkout)
    // ── Referred-By (admin-managed list, picked at checkout, snapshot stays stable forever) ──
    public Guid? ReferralSourceId { get; set; }
    public string? ReferralSourceName { get; set; }
    public ReferralSourceType? ReferralType { get; set; }
    public string? ReferralCustomText { get; set; }   // populated when ReferralType == Other
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public Guid? CouponId { get; set; }
    public string? CouponCode { get; set; }
    public decimal GstAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>For franchise orders only: the franchisee's TOTAL resolved share on this order —
    /// commission plus any GST on it. The franchisee keeps this by paying less, so it is deducted
    /// from what they pay. Zero for non-franchise orders.
    /// Always equals <see cref="FranchiseCommissionBase"/> + <see cref="FranchiseCommissionGst"/>
    /// on orders placed after the commission-GST split was introduced.</summary>
    public decimal FranchiseShareAmount { get; set; }

    /// <summary>The bare commission inside <see cref="FranchiseShareAmount"/>, before GST on it
    /// (₹20 in the specification's ₹118 example). This is the taxable value of the franchisee's
    /// own supply to the institute — the figure their commission invoice must state.
    /// Zero on orders written before this column existed; readers should fall back to
    /// <see cref="FranchiseShareAmount"/> when both split columns are zero.</summary>
    public decimal FranchiseCommissionBase { get; set; }

    /// <summary>GST charged on the commission (₹3.60 in the specification's example), and the
    /// input tax credit the institute can claim once the franchisee files it. Zero when the
    /// franchisee holds no GSTIN — they raise a bill of supply, not a tax invoice.</summary>
    public decimal FranchiseCommissionGst { get; set; }

    /// <summary>For franchise orders only: the amount the franchisee actually pays =
    /// <see cref="TotalAmount"/> − <see cref="FranchiseShareAmount"/>. Equals TotalAmount for
    /// non-franchise orders.</summary>
    public decimal FranchiseNetPayable { get; set; }

    /// <summary>True when the franchisee paid for this order out of their wallet. No tax invoice is
    /// raised for these — the money was already invoiced when it was added to the wallet (a
    /// <see cref="OrderSource.WalletTopUp"/> order). Gateway-paid franchise orders stay false and
    /// are invoiced normally.</summary>
    public bool PaidFromWallet { get; set; }

    // Checkout attributes selected at checkout (resolved JSON) and their summed price impact (already in TotalAmount).
    public string? CheckoutAttributesJson { get; set; }
    public decimal CheckoutAttributesAmount { get; set; }
    // Customer profile — Individual (default) or Organization. For Org, OrgName is set and StudentName
    // becomes the contact person name. Address fields below double as the billing address.
    public CustomerType CustomerType { get; set; } = CustomerType.Individual;
    public string? OrgName { get; set; }
    public string? BillingName { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }
    public string? GstNumber { get; set; }
    public string GstClassification { get; set; } = "B2C";

    /// <summary>True when GST liability is on the recipient under reverse charge (RCM). Shown on
    /// the receipt/invoice; the tax is still computed but flagged as payable under reverse charge.</summary>
    public bool ReverseCharge { get; set; }

    // Shipping address — populated only when different from billing.
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPincode { get; set; }
    public decimal ShippingCharges { get; set; }
    public string? CustomerNotes { get; set; }                 // notes from the customer (visible everywhere)
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public PaymentMode? PaymentMode { get; set; }

    /// <summary>The instrument the customer ACTUALLY paid with, as reported by the gateway after a
    /// successful payment — "UPI", "Credit Card", "Debit Card", "Net Banking", "Wallet", "EMI", …
    /// <para>Distinct from <see cref="PaymentMode"/>, which records the gateway/channel we sent them
    /// to (Razorpay / Easebuzz / Cash / Cheque). So a Razorpay order paid by UPI reads
    /// PaymentMode=Razorpay, GatewayPaymentMode="UPI". Null for offline orders and for any online
    /// payment whose instrument the gateway didn't report.</para></summary>
    public string? GatewayPaymentMode { get; set; }

    public string? InternalNotes { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    // Soft delete — excluded from all queries by a global filter; use IgnoreQueryFilters() to see deleted orders.
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public User? User { get; set; }
    public User? CreatedBy { get; set; }
    public Franchise? Franchise { get; set; }
    public Coupon? Coupon { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<OrderNote> Notes { get; set; } = new List<OrderNote>();
    public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
    public Invoice? Invoice { get; set; }
}
