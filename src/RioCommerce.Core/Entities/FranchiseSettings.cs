namespace RioCommerce.Core.Entities;

/// <summary>
/// Global, platform-wide franchise behaviour toggles (a single row). Governs auto-assignment
/// of products/franchises and how commission is computed. Loaded once and cached per request.
/// </summary>
public class FranchiseSettings : BaseEntity
{
    /// <summary>When a NEW product is created, auto-create commission rows for all active franchises
    /// (using the product's default share). Requirement 3.</summary>
    public bool AutoAssignNewProductsToAllFranchises { get; set; } = false;

    /// <summary>When a NEW franchise is created, auto-assign all active products to it
    /// (seeding rows from each product's default share where enabled). Requirement 4.</summary>
    public bool AutoAssignAllProductsToNewFranchise { get; set; } = false;

    /// <summary>Allow per-franchise override rows to take precedence over the product default.
    /// When false, the product default always wins regardless of any override row.</summary>
    public bool AllowFranchiseSpecificOverride { get; set; } = true;

    /// <summary>Apply the share on the special price while it's active (else always on regular).
    /// Requirement 2 / 5.</summary>
    public bool ApplyShareOnSpecialPrice { get; set; } = true;

    /// <summary>Master switch — when false, no automatic product/franchise assignment happens
    /// regardless of the two auto-assign flags above.</summary>
    public bool EnableAutomaticAssignment { get; set; } = true;

    /// <summary>GST rate (%) charged on a wallet top-up invoice. A top-up isn't tied to a product,
    /// so it can't inherit a product's rate — it needs one configured value. Applies only to
    /// <see cref="Enums.OrderSource.WalletTopUp"/> orders; course orders keep using each product's
    /// own rate.</summary>
    public decimal WalletInvoiceGstRate { get; set; } = 18.00m;
}
