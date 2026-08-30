# HJ Classes — CA Coaching Platform

ASP.NET Core 10 solution (clean architecture). The **RioCommerce.API** project is the host — it
serves both the REST API (`/api/*`) and the Blazor Server UI from a single process.

## Prerequisites
- .NET 10 SDK (all projects target `net10.0`)
- PostgreSQL running locally on `localhost:5432`

## Configuration
The connection string lives in [src/RioCommerce.API/appsettings.json](src/RioCommerce.API/appsettings.json):

```
Host=localhost;Port=5432;Database=riocommerce-custom;Username=postgres;Password=123456789
```

Adjust the host/credentials to match your local PostgreSQL. The database itself does **not** need
to be created manually — it's created and migrated automatically on startup.

## Quick Start
```bash
dotnet run --project src/RioCommerce.API
```
On startup the app:
- creates the database (if missing) and applies all migrations,
- seeds reference data (roles, faculty, categories, …),
- ensures the default admin account exists and can log in.

Open: https://localhost:5001 — API docs at https://localhost:5001/swagger

### Default admin login
| Field | Value |
| ----- | ----- |
| Email / Phone | `admin@riocommerce.com` or `9975242929` |
| Password | `Admin@123` |

The admin account self-heals on every startup: if the stored password can't log in, it's reset to
`Admin@123` (a deliberately-changed password is left untouched).

## Pages
- `/` — Home page
- `/courses/ca-foundation` — Course catalog
- `/course/{slug}` — Course detail
- `/login` — Login/Register
- `/admin/dashboard` — Admin panel
- `/admin/orders` — Order management
- `/admin/products` — Product management
- `/swagger` — API docs

## Database migrations (script-based)
Migrations are plain SQL scripts applied automatically on startup — EF Core migrations are
**not** used. On `dotnet run` the app:
- creates the database if it doesn't exist,
- applies any pending scripts from `src/RioCommerce.Infrastructure/Migrations/Scripts/` in
  file-name order, recording each in a `schema_migrations` table (name + checksum),
- on an existing database, the baseline (`0000_baseline.sql`) is auto-stamped as applied
  rather than re-run, so it never collides with live tables.

### Adding a schema change
Create a new script with the next numeric prefix, e.g.
`src/RioCommerce.Infrastructure/Migrations/Scripts/0001_add_widget_table.sql`:

```sql
CREATE TABLE IF NOT EXISTS widgets (
    "Id"   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" varchar(200) NOT NULL
);
```

Rules:
- Prefix dictates run order — always increment it (`0001`, `0002`, …).
- The script is embedded automatically (the .csproj globs `Migrations/Scripts/**/*.sql`).
- Prefer idempotent DDL (`IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`) so a re-run after a
  mid-script crash stays safe.
- **Never edit an already-applied script** — its checksum is recorded and a change is treated
  as a fatal error on next startup. Fix forward with a new script.

No `dotnet ef`, no model snapshot, no migration assembly. The `RioCommerceDbContext` is still
used for querying; it simply no longer owns the schema.
