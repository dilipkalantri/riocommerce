using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace RioCommerce.Infrastructure.Migrations;

/// <summary>
/// Script-based database migrator. Replaces EF Core migrations entirely.
///
/// <para>On <see cref="RunAsync"/> it:</para>
/// <list type="number">
///   <item>Ensures the <c>schema_migrations</c> bookkeeping table exists.</item>
///   <item>Discovers every embedded <c>*.sql</c> resource under
///         <c>RioCommerce.Infrastructure/Migrations/Scripts/</c>, ordered by file name.</item>
///   <item>Runs each script that hasn't been recorded yet, inside its own transaction,
///         then records its name + SHA-256 checksum.</item>
/// </list>
///
/// <para>Naming convention: <c>NNNN_description.sql</c> (e.g. <c>0000_baseline.sql</c>,
/// <c>0001_add_widget_table.sql</c>). The numeric prefix dictates run order, so always
/// increment it for new scripts. Scripts must be embedded resources — the .csproj globs
/// <c>Migrations/Scripts/**/*.sql</c> as <c>&lt;EmbeddedResource&gt;</c>.</para>
///
/// <para>Idempotency is the script author's responsibility for any script AFTER the baseline:
/// prefer <c>CREATE TABLE IF NOT EXISTS</c>, <c>ADD COLUMN IF NOT EXISTS</c>, etc. The runner
/// guarantees a script runs at most once, but a script that half-applied before a crash will
/// re-run from the top, so defensive DDL keeps re-runs safe.</para>
///
/// <para>A checksum mismatch (an already-applied script whose text later changed) is treated as
/// a fatal configuration error — applied migrations are immutable. Fix forward with a new script
/// rather than editing a recorded one.</para>
/// </summary>
public sealed class SqlMigrationRunner
{
    private const string ScriptResourcePrefix = "RioCommerce.Infrastructure.Migrations.Scripts.";

    /// <summary>Friendly name of the baseline script (matches the embedded file name).</summary>
    public const string BaselineScriptName = "0000_baseline.sql";

    private readonly string _connectionString;
    private readonly ILogger<SqlMigrationRunner> _log;

    public SqlMigrationRunner(string connectionString, ILogger<SqlMigrationRunner> log)
    {
        _connectionString = connectionString;
        _log = log;
    }

    /// <summary>Exposes the connection string for the one-time baseline bootstrap. Read-only.</summary>
    public string ConnectionStringForBootstrap => _connectionString;

    /// <summary>SHA-256 checksum of the embedded baseline script — used by the bootstrap to stamp
    /// existing databases with a row that matches what the runner would otherwise compute.</summary>
    public string BaselineChecksum
    {
        get
        {
            var script = DiscoverScripts().FirstOrDefault(s => s.Name == BaselineScriptName);
            if (script.Sql is null)
                throw new InvalidOperationException($"Baseline script '{BaselineScriptName}' not found among embedded resources.");
            return Sha256(script.Sql);
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseExistsAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await EnsureBookkeepingTableAsync(conn, ct);
        var applied = await LoadAppliedAsync(conn, ct);

        var scripts = DiscoverScripts();
        if (scripts.Count == 0)
        {
            _log.LogWarning("SqlMigrationRunner: no embedded migration scripts found.");
            return;
        }

        var ranThisStartup = 0;
        foreach (var (name, sql) in scripts)
        {
            var checksum = Sha256(sql);

            if (applied.TryGetValue(name, out var existingChecksum))
            {
                if (!string.Equals(existingChecksum, checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Migration '{name}' has already been applied but its checksum has changed. " +
                        "Applied migrations are immutable — create a new forward script instead of editing this one.");
                }
                continue; // already applied, unchanged — skip.
            }

            _log.LogInformation("SqlMigrationRunner: applying {Script}…", name);
            await ApplyScriptAsync(conn, name, sql, checksum, ct);
            ranThisStartup++;
            _log.LogInformation("SqlMigrationRunner: applied {Script}.", name);
        }

        if (ranThisStartup == 0)
            _log.LogInformation("SqlMigrationRunner: database up to date ({Count} scripts, nothing to apply).", scripts.Count);
        else
            _log.LogInformation("SqlMigrationRunner: applied {Ran} new script(s).", ranThisStartup);

        // The baseline (and future scripts) may create/alter PostgreSQL enum types. Npgsql caches
        // the DB type catalog per physical connection; refresh it so reads later in this same
        // process map enum OIDs/labels correctly rather than failing on a stale catalog.
        await conn.ReloadTypesAsync(ct);
    }

    // ── Database existence (replaces EnsureCreated/Migrate's auto-create) ────────────────────

    private async Task EnsureDatabaseExistsAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var targetDb = builder.Database;
        if (string.IsNullOrWhiteSpace(targetDb))
            throw new InvalidOperationException("Connection string has no Database specified.");

        // Connect to the maintenance 'postgres' database to check for / create the target.
        var adminBuilder = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        try
        {
            await admin.OpenAsync(ct);
        }
        catch (Exception ex)
        {
            // If we can't reach the maintenance DB, fall through — the main connection below will
            // surface a clear error. This keeps environments that restrict the postgres DB working
            // as long as the target DB already exists.
            _log.LogWarning(ex, "SqlMigrationRunner: could not connect to maintenance DB to verify existence; assuming target exists.");
            return;
        }

        await using (var check = admin.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            check.Parameters.AddWithValue("name", targetDb);
            var exists = await check.ExecuteScalarAsync(ct) is not null;
            if (exists) return;
        }

        _log.LogInformation("SqlMigrationRunner: database '{Db}' not found — creating it.", targetDb);
        await using var create = admin.CreateCommand();
        // Database names can't be parameterised; quote-escape defensively.
        create.CommandText = $"CREATE DATABASE \"{targetDb.Replace("\"", "\"\"")}\"";
        await create.ExecuteNonQueryAsync(ct);
    }

    // ── Bookkeeping ──────────────────────────────────────────────────────────────────────────

    private static async Task EnsureBookkeepingTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                script_name  varchar(260)  PRIMARY KEY,
                checksum     varchar(64)   NOT NULL,
                applied_at   timestamptz   NOT NULL DEFAULT now()
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, string>> LoadAppliedAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT script_name, checksum FROM schema_migrations";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static async Task ApplyScriptAsync(
        NpgsqlConnection conn, string name, string sql, string checksum, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var run = conn.CreateCommand())
            {
                run.Transaction = tx;
                run.CommandText = sql;
                run.CommandTimeout = 0; // no timeout — a baseline can be large.
                await run.ExecuteNonQueryAsync(ct);
            }

            await using (var record = conn.CreateCommand())
            {
                record.Transaction = tx;
                record.CommandText =
                    "INSERT INTO schema_migrations (script_name, checksum) VALUES (@n, @c)";
                record.Parameters.AddWithValue("n", name);
                record.Parameters.AddWithValue("c", checksum);
                await record.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Script discovery ─────────────────────────────────────────────────────────────────────

    private static List<(string Name, string Sql)> DiscoverScripts()
    {
        var asm = typeof(SqlMigrationRunner).Assembly;
        var names = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ScriptResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var scripts = new List<(string, string)>(names.Count);
        foreach (var resource in names)
        {
            using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Could not open embedded migration resource '{resource}'.");
            using var sr = new StreamReader(stream, Encoding.UTF8);
            var sql = sr.ReadToEnd();
            // Friendly name = the file name without the resource namespace prefix.
            var friendly = resource[ScriptResourcePrefix.Length..];
            scripts.Add((friendly, sql));
        }
        return scripts;
    }

    private static string Sha256(string text)
    {
        // Normalize line endings before hashing so the checksum is independent of whether the
        // script file is stored CRLF or LF. This makes the migration guard immune to line-ending
        // drift across machines/OSes (the recurring "checksum has changed" false positive) — a
        // script's identity is its content, not its newline style. Collapse CRLF and lone CR to LF.
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes); // upper-case hex, 64 chars.
    }
}
