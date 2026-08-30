-- ============================================================================
-- 0007_banner_management.sql
-- Upgrades the existing public."Banners" table to a NopCommerce "Anywhere
-- Slider"-style homepage banner manager with separate desktop / mobile images,
-- optional title/description/CTA button, and a scheduling window.
--
-- Also adds a single-row banner_slider_settings table for global carousel
-- behaviour (autoplay, interval, arrows, dots) editable from the admin UI.
--
-- Idempotent: every change is guarded (ADD COLUMN IF NOT EXISTS / CREATE TABLE
-- IF NOT EXISTS / conditional INSERT), so a re-run after a mid-script crash is
-- safe. Column casing matches the existing quoted-PascalCase convention on the
-- "Banners" table (EF default mapping for this entity).
-- ============================================================================

-- ── New columns on the existing "Banners" table ────────────────────────────
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "Name"            text;
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "DesktopImageUrl" text;
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "MobileImageUrl"  text;
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "Description"     text;
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "ButtonText"     text;
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "StartDate"      timestamp with time zone;
ALTER TABLE public."Banners" ADD COLUMN IF NOT EXISTS "EndDate"        timestamp with time zone;

-- Backfill: existing rows keep their single image as the desktop image, and get
-- a sensible internal name (their Title, else a generic label).
UPDATE public."Banners"
SET "DesktopImageUrl" = "ImageUrl"
WHERE "DesktopImageUrl" IS NULL AND "ImageUrl" IS NOT NULL AND "ImageUrl" <> '';

UPDATE public."Banners"
SET "Name" = COALESCE(NULLIF("Title", ''), 'Banner')
WHERE "Name" IS NULL OR "Name" = '';

-- The new entity treats Name as required (non-null app-side). Existing rows are
-- now backfilled, so enforce it going forward without breaking the migration.
ALTER TABLE public."Banners" ALTER COLUMN "Name" SET DEFAULT '';
UPDATE public."Banners" SET "Name" = '' WHERE "Name" IS NULL;
ALTER TABLE public."Banners" ALTER COLUMN "Name" SET NOT NULL;

-- Active banners are queried by placement + display order on every storefront
-- page render, so index that hot path.
CREATE INDEX IF NOT EXISTS "IX_Banners_Placement_DisplayOrder"
    ON public."Banners" ("Placement", "DisplayOrder");

-- ── Global slider settings (single row) ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS public.banner_slider_settings (
    "Id"                    uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "Autoplay"              boolean                  NOT NULL DEFAULT true,
    "AutoplayIntervalMs"    integer                  NOT NULL DEFAULT 5000,
    "ShowArrows"            boolean                  NOT NULL DEFAULT true,
    "ShowDots"              boolean                  NOT NULL DEFAULT true,
    "PauseOnHover"          boolean                  NOT NULL DEFAULT true,
    "RecommendedDesktopW"   integer                  NOT NULL DEFAULT 1920,
    "RecommendedDesktopH"   integer                  NOT NULL DEFAULT 600,
    "RecommendedMobileW"    integer                  NOT NULL DEFAULT 768,
    "RecommendedMobileH"    integer                  NOT NULL DEFAULT 900,
    "CreatedAt"             timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"             timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_banner_slider_settings" PRIMARY KEY ("Id")
);

-- Seed the single settings row if none exists.
INSERT INTO public.banner_slider_settings ("Id")
SELECT gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM public.banner_slider_settings);
