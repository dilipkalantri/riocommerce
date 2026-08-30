-- ============================================================================
-- 0016_add_seo_url_records.sql
-- Global public-URL registry: ONE PUBLIC URL = ONE OWNER.
-- A single table maps a normalized root slug → its owning entity, with a DB
-- UNIQUE index on NormalizedSlug so a duplicate is physically impossible even
-- under concurrent writes. The root slug resolver reads it on the hot path.
--
-- Backfill registers every live Category and Product. A pre-migration scan
-- confirmed there are NO duplicate root slugs, so nothing is renamed here.
--
-- Idempotent: IF NOT EXISTS + ON CONFLICT DO NOTHING throughout.
-- ============================================================================

CREATE TABLE IF NOT EXISTS public.seo_url_records (
    "Id"             uuid         NOT NULL DEFAULT gen_random_uuid(),
    "Slug"           varchar(300) NOT NULL,
    "NormalizedSlug" varchar(300) NOT NULL,
    "EntityType"     varchar(50)  NOT NULL,
    "EntityId"       uuid         NOT NULL,
    "EntityName"     text,
    "IsActive"       boolean      NOT NULL DEFAULT true,
    "CreatedAt"      timestamptz  NOT NULL DEFAULT now(),
    "UpdatedAt"      timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT "PK_seo_url_records" PRIMARY KEY ("Id")
);

-- MANDATORY global uniqueness on the normalized public slug.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_seo_url_records_NormalizedSlug"
    ON public.seo_url_records ("NormalizedSlug");
-- One registry row per owner.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_seo_url_records_Entity"
    ON public.seo_url_records ("EntityType", "EntityId");

-- ── Backfill live Categories + Products (skips anything already registered / colliding) ──
INSERT INTO seo_url_records ("Slug", "NormalizedSlug", "EntityType", "EntityId", "EntityName", "IsActive", "CreatedAt", "UpdatedAt")
SELECT src.ns, src.ns, src.t, src.id, src.name, true, now(), now()
FROM (
    SELECT lower(regexp_replace(trim(both '/' from trim("Slug")), '-+', '-', 'g')) AS ns,
           'Category' AS t, "Id" AS id, "Name" AS name
    FROM public."Categories"
    WHERE "Slug" IS NOT NULL AND btrim("Slug") <> ''
    UNION ALL
    SELECT lower(regexp_replace(trim(both '/' from trim("Slug")), '-+', '-', 'g')),
           'Product', "Id", "Title"
    FROM public.products
    WHERE "Slug" IS NOT NULL AND btrim("Slug") <> ''
) src
WHERE src.ns <> ''
  AND NOT EXISTS (SELECT 1 FROM seo_url_records e WHERE e."NormalizedSlug" = src.ns)
  AND NOT EXISTS (SELECT 1 FROM seo_url_records e WHERE e."EntityType" = src.t AND e."EntityId" = src.id)
ON CONFLICT DO NOTHING;

-- ── Migrate stored menu URLs to the clean form so the header links directly (no 301 hop) ──
UPDATE menu_items SET "Url" = '/' || substring("Url" from '^/category/(.*)$'), "UpdatedAt" = now()
WHERE "Url" LIKE '/category/%';
UPDATE menu_items SET "Url" = '/' || substring("Url" from '^/course/(.*)$'), "UpdatedAt" = now()
WHERE "Url" LIKE '/course/%';
