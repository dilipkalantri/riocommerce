-- ============================================================================
-- 0010_legacy_migration_maps.sql
-- Old (nopCommerce 4.5 / SQL Server) → new system migration mapping tables.
-- Stores a permanent old↔new id mapping for products and customers/users so
-- records can be traced across systems and the importer stays idempotent
-- (re-running upserts the same row instead of duplicating).
--
-- Idempotent: CREATE TABLE IF NOT EXISTS. Safe to re-run.
-- ============================================================================

CREATE TABLE IF NOT EXISTS public.legacy_product_map (
    "Id"          uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "LegacyId"    integer                  NOT NULL,           -- old Product.Id
    "NewId"       uuid                     NOT NULL,           -- new products."Id"
    "LegacyKey"   text,                                        -- old Sku (natural key used for matching)
    "ImportedAt"  timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_legacy_product_map" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_legacy_product_map_LegacyId"
    ON public.legacy_product_map ("LegacyId");
CREATE INDEX IF NOT EXISTS "IX_legacy_product_map_NewId"
    ON public.legacy_product_map ("NewId");

CREATE TABLE IF NOT EXISTS public.legacy_user_map (
    "Id"          uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "LegacyId"    integer                  NOT NULL,           -- old Customer.Id
    "NewId"       uuid                     NOT NULL,           -- new users."Id"
    "LegacyKey"   text,                                        -- old email (natural key used for matching)
    "ImportedAt"  timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_legacy_user_map" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_legacy_user_map_LegacyId"
    ON public.legacy_user_map ("LegacyId");
CREATE INDEX IF NOT EXISTS "IX_legacy_user_map_NewId"
    ON public.legacy_user_map ("NewId");
