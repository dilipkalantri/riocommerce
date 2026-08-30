using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace RioCommerce.Infrastructure.Migrations;

/// <summary>
/// Registration + invocation helpers for the script-based migrator. Keeps <c>Program.cs</c>
/// to two calls: <c>AddSqlMigrations(connectionString)</c> at configuration time and
/// <c>await app.Services.MigrateDatabaseAsync()</c> at startup, before any seeding.
/// </summary>
public static class SqlMigrationServiceCollectionExtensions
{
    public static IServiceCollection AddSqlMigrations(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(sp => new SqlMigrationRunner(
            connectionString,
            sp.GetRequiredService<ILogger<SqlMigrationRunner>>()));
        return services;
    }

    /// <summary>
    /// Runs all pending SQL migrations. Call once at startup before seeding.
    ///
    /// <para>On an EXISTING database that was previously managed by EF Core, the baseline
    /// script must be marked as already-applied so it doesn't try to recreate live tables.
    /// This method detects that case automatically: if the schema already exists (the legacy
    /// <c>__EFMigrationsHistory</c> table is present) but the baseline hasn't been recorded
    /// in <c>schema_migrations</c>, it records the baseline as applied WITHOUT running it.
    /// Fresh databases skip this and run the baseline normally.</para>
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<SqlMigrationRunner>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SqlMigrationRunner>>();

        await BaselineExistingDatabaseAsync(scope.ServiceProvider, logger, ct);
        await runner.RunAsync(ct);
    }

    /// <summary>
    /// One-time reconciliation for databases that predate the script-based migrator. If the
    /// schema is already present (legacy EF history table exists) but our bookkeeping doesn't
    /// yet know about the baseline, stamp the baseline as applied so the runner skips it.
    /// Computes the baseline's checksum from the embedded resource so the stamped row matches
    /// exactly what the runner would compute, keeping the checksum guard happy.
    /// </summary>
    private static async Task BaselineExistingDatabaseAsync(
        IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        var runner = sp.GetRequiredService<SqlMigrationRunner>();
        var connectionString = runner.ConnectionStringForBootstrap;

        await using var conn = new NpgsqlConnection(connectionString);
        try
        {
            await conn.OpenAsync(ct);
        }
        catch
        {
            // Database doesn't exist yet (fresh install) — nothing to baseline; the runner will
            // create the DB and run the baseline normally.
            return;
        }

        // Ensure bookkeeping table exists so we can query/insert.
        await using (var ensure = conn.CreateCommand())
        {
            ensure.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    script_name  varchar(260)  PRIMARY KEY,
                    checksum     varchar(64)   NOT NULL,
                    applied_at   timestamptz   NOT NULL DEFAULT now()
                );
                """;
            await ensure.ExecuteNonQueryAsync(ct);
        }

        // Is the baseline already recorded? If so, nothing to do.
        await using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM schema_migrations WHERE script_name = @n";
            check.Parameters.AddWithValue("n", SqlMigrationRunner.BaselineScriptName);
            if (await check.ExecuteScalarAsync(ct) is not null) return;
        }

        // Baseline not recorded. Is this a pre-existing (EF-managed) schema, or a fresh DB?
        bool legacySchemaPresent;
        await using (var legacy = conn.CreateCommand())
        {
            legacy.CommandText = "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL";
            legacySchemaPresent = await legacy.ExecuteScalarAsync(ct) is bool b && b;
        }

        // Fallback signal: even without the EF history table, if a core table like users exists,
        // treat the schema as pre-existing so we never run the baseline over live data.
        if (!legacySchemaPresent)
        {
            await using var core = conn.CreateCommand();
            core.CommandText = "SELECT to_regclass('public.users') IS NOT NULL";
            legacySchemaPresent = await core.ExecuteScalarAsync(ct) is bool b && b;
        }

        if (!legacySchemaPresent) return; // fresh DB — let the runner apply the baseline.

        // Pre-existing schema: stamp the baseline as applied (do NOT run it).
        var checksum = runner.BaselineChecksum;
        await using (var stamp = conn.CreateCommand())
        {
            stamp.CommandText =
                "INSERT INTO schema_migrations (script_name, checksum) VALUES (@n, @c) " +
                "ON CONFLICT (script_name) DO NOTHING";
            stamp.Parameters.AddWithValue("n", SqlMigrationRunner.BaselineScriptName);
            stamp.Parameters.AddWithValue("c", checksum);
            await stamp.ExecuteNonQueryAsync(ct);
        }
        logger.LogInformation(
            "SqlMigrationRunner: existing schema detected — baseline '{Baseline}' stamped as applied (not executed).",
            SqlMigrationRunner.BaselineScriptName);
    }
}
