using RioCommerce.Core.DTOs.Migration;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// One-time data migration from the old nopCommerce 4.5 (SQL Server) database into this system.
/// Reads from SQL Server, upserts products and customers→users into PostgreSQL, and records an
/// old↔new id map per entity. Idempotent: products match on SKU, users on email, so re-runs update
/// rather than duplicate. Passwords are NOT migrated (incompatible hashes) — imported users land
/// with a null password, and the first time one of them tries to log in, /account/login recognises
/// the account by its legacy_user_map row and steers them to /set-password to create one after an
/// email OTP. Old orders are intentionally not migrated.
/// </summary>
public interface ILegacyMigrationService
{
    Task<ImportRunResult> RunAsync(ImportRequest request, Guid? actorId, string? actorName, CancellationToken ct = default);
}
