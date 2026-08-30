using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

// One service that renders reports to Excel or PDF, plus per-order invoice PDFs.
// `franchiseScopeId` is honored on franchisee-side calls to scope all queries to a single franchise;
// pass null on admin-side endpoints to allow filter.FranchiseId to control scope.
public interface IExportService
{
    Task<(byte[] bytes, string filename)> OrdersAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);
    Task<(byte[] bytes, string filename)> CommissionAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);

    /// <summary>
    /// Earned faculty shares from the <c>FacultyShareEntries</c> ledger, decomposed into the bare
    /// share, the GST on it, and the total payout — the three figures a fee invoice and an input-tax-
    /// credit claim each need. Admin-only: <paramref name="franchiseScopeId"/> is accepted for
    /// signature symmetry but a franchisee has no business seeing faculty remuneration, so a scoped
    /// call returns an empty report rather than someone else's payroll.
    /// </summary>
    Task<(byte[] bytes, string filename)> FacultyShareAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);
    Task<(byte[] bytes, string filename)> WalletAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);
    Task<(byte[] bytes, string filename)> CustomersAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);
    Task<(byte[] bytes, string filename)> GstAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);

    /// <summary>GST on sales to GSTIN-holding buyers. Same rows as <see cref="GstAsync"/> filtered to
    /// B2B, plus GSTIN, place of supply and reverse-charge columns.</summary>
    Task<(byte[] bytes, string filename)> B2bAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);

    /// <summary>GST on sales to unregistered buyers (no GSTIN captured at checkout).</summary>
    Task<(byte[] bytes, string filename)> B2cAsync(ReportFilter filter, bool excel, Guid? franchiseScopeId);

    Task<(byte[] bytes, string filename)?> InvoicePdfAsync(Guid orderId, Guid? franchiseScopeId);
}
