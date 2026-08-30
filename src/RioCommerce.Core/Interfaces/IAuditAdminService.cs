using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Backs the /admin/audit page. All routes are Super-Admin only — staff cannot read, edit, or
/// delete audit records. Filter / page / sort are pushed to PostgreSQL via composite indexes.
/// </summary>
public interface IAuditAdminService
{
    Task<PagedResult<AuditRow>> ListAsync(AuditFilter filter);
    Task<AuditSummary> SummaryAsync();
    Task<AuditDistinct> DistinctsAsync();
    Task<AuditRow?> GetAsync(Guid id);
    Task<string> ExportCsvAsync(AuditFilter filter);
}
