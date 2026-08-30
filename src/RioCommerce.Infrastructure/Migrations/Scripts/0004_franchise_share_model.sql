-- ============================================================================
-- 0004_franchise_share_model.sql
-- Replaces the per-product FranchisePrice margin model with a configurable
-- default franchise share, and adds global franchise settings.
--   • products: + EnableDefaultFranchiseShare, DefaultFranchiseShareType,
--                 DefaultFranchiseShareValue;  - FranchisePrice
--   • franchise_settings: new single-row global toggles table
--
-- Idempotent: safe to re-run.
-- ============================================================================

ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "EnableDefaultFranchiseShare" boolean NOT NULL DEFAULT false;
ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "DefaultFranchiseShareType" integer NOT NULL DEFAULT 0;   -- 0 = Percent, 1 = Fixed
ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "DefaultFranchiseShareValue" numeric(10,2) NOT NULL DEFAULT 0;

-- Best-effort migration of the old margin model: where a FranchisePrice existed and was below
-- the selling price, seed a fixed default share equal to that per-unit margin so existing
-- products keep an equivalent commission until an admin reviews them.
UPDATE public.products
SET "DefaultFranchiseShareType" = 1,                                   -- Fixed
    "DefaultFranchiseShareValue" = GREATEST(0, "SellingPrice" - "FranchisePrice"),
    "EnableDefaultFranchiseShare" = true
WHERE "FranchisePrice" IS NOT NULL
  AND "FranchisePrice" < "SellingPrice";

ALTER TABLE public.products DROP COLUMN IF EXISTS "FranchisePrice";

CREATE TABLE IF NOT EXISTS public.franchise_settings (
    "Id"                                 uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "AutoAssignNewProductsToAllFranchises" boolean                NOT NULL DEFAULT false,
    "AutoAssignAllProductsToNewFranchise"  boolean                NOT NULL DEFAULT false,
    "AllowFranchiseSpecificOverride"     boolean                  NOT NULL DEFAULT true,
    "ApplyShareOnSpecialPrice"           boolean                  NOT NULL DEFAULT true,
    "EnableAutomaticAssignment"          boolean                  NOT NULL DEFAULT true,
    "CreatedAt"                          timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"                          timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_franchise_settings" PRIMARY KEY ("Id")
);

-- Seed the single settings row if none exists.
INSERT INTO public.franchise_settings ("Id")
SELECT gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM public.franchise_settings);
