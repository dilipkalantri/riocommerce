using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

public interface IFranchiseService
{
    Task<FranchiseStats> StatsAsync();
    Task<List<FranchiseCard>> FranchisesAsync();

    /// <summary>Bulk-import already-trusted franchisees (e.g. migrated from the old ERP). Creates a
    /// fully-provisioned, Approved franchise + franchise_admin login user + UserRole link per row,
    /// mirroring ApproveAsync — but WITHOUT OTP, generated password, or notification email
    /// (imported users set their password via forgot-password). Wallet/credit stay 0. Idempotent:
    /// matches on ContactEmail, updates the existing franchise rather than duplicating.</summary>
    /// <summary>Parse an uploaded franchisee .xlsx (the legacy ERP export) into import rows,
    /// mapping the known columns (Name, Description→BusinessName, Email/Username, Mobile, Address,
    /// City, State Name, Gstin, Pan, Pincode). Financial/bank columns are ignored.</summary>
    Task<List<FranchiseImportRow>> ParseFranchiseExcelAsync(Stream xlsx, CancellationToken ct = default);

    Task<FranchiseImportResult> BulkImportAsync(FranchiseImportRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<PagedResult<FranchiseOrderRow>> OrdersAsync(Guid? franchiseId, OrderStatus? status, int page, int pageSize);
    Task<(bool ok, string? error)> TopUpAsync(Guid franchiseId, decimal amount, string? note);
    Task<(bool ok, string? error)> SetCreditLimitAsync(Guid franchiseId, decimal limit);

    // Phase 1 — Onboarding & lifecycle
    Task<(bool ok, string? error, Guid? id)> RegisterAsync(FranchiseRegistrationRequest req);
    Task<(bool ok, string? error)> VerifyApplicationAsync(string target, string code);
    /// <summary>Admin override: moves an Unverified application straight to Pending without the applicant's
    /// OTP, so an admin can review + approve/reject a freshly-registered application. Idempotent.</summary>
    Task<(bool ok, string? error)> AdminVerifyApplicationAsync(Guid franchiseId, Guid actorUserId);

    /// <summary>Resend the application verification code to a still-unverified application's email/phone.</summary>
    Task<(bool ok, string? error)> ResendApplicationCodeAsync(string email);
    Task<PagedResult<FranchiseApplicationRow>> ListApplicationsAsync(FranchiseStatus? status, string? search, int page, int pageSize);
    Task<FranchiseAdminDetail?> GetDetailAsync(Guid id);
    /// <summary>Read-only: reports any existing account or franchise already holding this
    /// franchise's contact details, so the approval screen can ask before anything is written.</summary>
    Task<FranchiseApprovalConflict> CheckApprovalConflictAsync(Guid franchiseId, CancellationToken ct = default);

    Task<(bool ok, string? error)> ApproveAsync(FranchiseApprovalRequest req, Guid actorUserId);
    Task<(bool ok, string? error)> RejectAsync(FranchiseRejectionRequest req, Guid actorUserId);
    /// <summary>Updates a franchise's own particulars (GSTIN, PAN, address, contact person).
    /// Carries no money and no lifecycle — wallet, credit limit, commissions, status, code and
    /// login email are all out of its reach.</summary>
    Task<(bool ok, string? error)> UpdateProfileAsync(FranchiseProfileEdit req, Guid actorUserId);

    Task<(bool ok, string? error)> SetActiveAsync(Guid franchiseId, bool isActive, Guid actorUserId);
    Task<(bool ok, string? error)> ResetPasswordAsync(Guid franchiseId, Guid actorUserId);

    // Phase 2 — Per-franchise product commission
    Task<List<FranchiseCommissionRow>> ListCommissionsAsync(Guid franchiseId);
    Task<(bool ok, string? error)> SetCommissionAsync(SetCommissionRequest req, Guid actorUserId);
    Task<(bool ok, string? error, int updated)> BulkSetCommissionsAsync(BulkSetCommissionRequest req, Guid actorUserId);

    /// <summary>Import product↔franchisee share rules from the old ERP (SQL Server). Reads
    /// dbo.ProductFranchiseeShare (IsActive=1), resolves ERP ProductId→SKUCode→new Product and
    /// ERP FranchiseeId→Email→new Franchise, maps eSharingType (1=Percent, 2=Fixed) + SharingAmount,
    /// and upserts via SetCommissionAsync. Unmatched product/franchise → skipped+flagged.</summary>
    Task<ErpShareImportResult> ImportErpFranchiseSharesAsync(ErpShareImportRequest request, Guid actorUserId, CancellationToken ct = default);
    /// <summary>Assign the same product↔commission rule across many (or all) franchisees in one call.</summary>
    Task<(bool ok, string? error, int updated)> BulkAssignAcrossFranchiseesAsync(BulkAssignAcrossFranchiseesRequest req, Guid actorUserId);

    /// <summary>Global franchise settings (single row).</summary>
    Task<FranchiseSettings> GetSettingsAsync();
    Task SaveSettingsAsync(FranchiseSettings settings, Guid actorUserId);
    /// <summary>When a new product is created with the default share enabled, seed commission rows
    /// for all active franchises (honouring global auto-assign settings). Returns rows created.</summary>
    Task<int> AutoAssignProductToFranchisesAsync(Guid productId);
    /// <summary>When a new franchise is created/approved, seed commission rows from every active
    /// product's default share (honouring global auto-assign settings). Returns rows created.</summary>
    Task<int> AutoAssignProductsToFranchiseAsync(Guid franchiseId);
    Task<(bool ok, string? error)> ClearCommissionAsync(Guid franchiseId, Guid productId, Guid actorUserId);

    // ── Assigned-products management grid (bulk-assign page) ──
    /// <summary>All existing franchise↔product assignments (commission rows), newest first.</summary>
    Task<List<FranchiseAssignmentRow>> ListAssignmentsAsync();
    /// <summary>Edit the share % / fixed value for one franchise↔product assignment (no reassignment).</summary>
    Task<(bool ok, string? error)> UpdateAssignmentAsync(UpdateAssignmentRequest req, Guid actorUserId);
    /// <summary>Enable or disable an assignment without deleting it.</summary>
    Task<(bool ok, string? error)> ToggleAssignmentAsync(Guid franchiseId, Guid productId, bool active, Guid actorUserId);
    /// <summary>Permanently remove an assignment row.</summary>
    Task<(bool ok, string? error)> RemoveAssignmentAsync(Guid franchiseId, Guid productId, Guid actorUserId);
    Task<PagedResult<FranchiseEarningRow>> GetEarningsAsync(Guid franchiseId, DateTime? from, DateTime? to, int page, int pageSize);
    Task<FranchiseEarningsSummary> GetEarningsSummaryAsync(Guid franchiseId);

    // Called by FranchisePortalService after a wallet-debit franchise order is committed; records one
    // FranchiseCommissionEntry per order item using the active rule (falling back to the implicit margin).
    Task RecordOrderCommissionAsync(Guid orderId);

    // Phase 3 — Wallet self-service recharge + admin manual adjustment
    Task<(bool ok, string? error, RechargeIntent? intent)> InitiateRechargeAsync(Guid franchiseId, decimal amount);
    Task<(bool ok, string? error, decimal newBalance)> ConfirmRechargeAsync(Guid franchiseId, ConfirmRechargeRequest req);
    Task<List<WalletRechargeRow>> ListRechargesAsync(Guid franchiseId, int take = 50);
    Task<string> GetStatementCsvAsync(Guid franchiseId, DateTime? from, DateTime? to);
    /// <summary>Admin-side wallet statement for one franchise: balance, credit limit, period totals
    /// and the ledger rows (newest first, capped by <paramref name="take"/>). Null when the franchise
    /// doesn't exist. Order-linked rows carry the order number so admins can trace a deduction.</summary>
    Task<FranchiseWalletStatement?> WalletStatementAsync(Guid franchiseId, DateTime? from = null, DateTime? to = null, int take = 500);
    Task<(bool ok, string? error)> AdjustWalletAsync(AdjustWalletRequest req, Guid actorUserId);
}
