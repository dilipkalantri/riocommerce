# RioCommerce — EF → Script-based migrations: apply guide

This package converts the project from EF Core migrations to SQL-script migrations applied
automatically at startup. Below is exactly what to add, replace, and delete in your solution,
plus one safety check.

## 1. Add / replace these files
Copy the contents of `src/` over your solution (same relative paths):

| File | Action |
| ---- | ------ |
| `src/RioCommerce.Infrastructure/Migrations/SqlMigrationRunner.cs` | **NEW** |
| `src/RioCommerce.Infrastructure/Migrations/SqlMigrationServiceCollectionExtensions.cs` | **NEW** |
| `src/RioCommerce.Infrastructure/Migrations/Scripts/0000_baseline.sql` | **NEW** (the v0 schema, from your live DB) |
| `src/RioCommerce.API/Program.cs` | **REPLACE** |
| `src/RioCommerce.API/RioCommerce.API.csproj` | **REPLACE** |
| `src/RioCommerce.Infrastructure/RioCommerce.Infrastructure.csproj` | **REPLACE** |
| `src/RioCommerce.Infrastructure/Data/Configurations/AppLogConfiguration.cs` | **REPLACE** (comment update only) |
| `README.md` | **REPLACE** (migration docs only) |

## 2. Delete these (obsolete)
- **All EF migration files**: everything in `src/RioCommerce.Infrastructure/Migrations/`
  EXCEPT the new `SqlMigrationRunner.cs`, `SqlMigrationServiceCollectionExtensions.cs`, and the
  `Scripts/` folder. That means every `*_*.cs` / `*_*.Designer.cs` migration and
  `RioCommerceDbContextModelSnapshot.cs`.
- `src/RioCommerce.API/dotnet-tools.json` (the `dotnet-ef` CLI manifest).
- The root `*.sql` hand-patch files: `CREATE-APP-LOGS-TABLE.sql`, `CREATE-INVOICES-TABLES.sql`,
  `CREATE-REVENUE-SHARING-UPDATE.sql`, `CREATE-SERIAL-KEY-TABLES.sql`, `FIX-INVOICE-NULLABLE.sql`,
  `FIX-INVOICEDATE-COLUMN.sql`, `FIX-PAYMENTMODE-COLUMN.sql`.
  (Their schema is already captured in `0000_baseline.sql`.)

## 3. Restore packages
`Microsoft.EntityFrameworkCore.Design` and `.Tools` were removed (only needed for `dotnet ef`).
Run `dotnet restore` then build.

## 4. How it behaves on first run
- **Your existing DB**: the runner sees the schema already exists (it finds `__EFMigrationsHistory`
  / `users`), so it stamps `0000_baseline.sql` as applied in a new `schema_migrations` table
  WITHOUT running it. Nothing collides. The old `__EFMigrationsHistory` table is left untouched
  and simply unused — you may drop it manually later if you wish.
- **A fresh DB**: the runner creates the database and runs `0000_baseline.sql` in full.

## 5. Safety check (recommended, do once)
The baseline came from a `pg_dump` of your live DB, so it should match exactly. To be certain the
stamp-not-run path triggers on your existing DB, confirm a core table is present before first run:

```sql
SELECT to_regclass('public.users') IS NOT NULL AS users_exists;  -- expect: true
```

After the first startup, verify the bookkeeping row was created and the baseline was NOT executed
against your live tables (no errors, app boots):

```sql
SELECT script_name, applied_at FROM schema_migrations;  -- expect: one row, 0000_baseline.sql
```

## 6. Adding future schema changes
Create `src/RioCommerce.Infrastructure/Migrations/Scripts/0001_<description>.sql` (increment the
prefix). It's embedded automatically. Use idempotent DDL (`IF NOT EXISTS`). Never edit an
already-applied script — the checksum guard will halt startup if you do; fix forward with a new
script instead.
