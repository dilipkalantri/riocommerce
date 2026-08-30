-- ============================================================================
-- 0012_product_option_groups.sql
-- Adds configurable per-product purchase-option groups (e.g. "Books", "Test
-- Series"). Each group has options; each option carries a PriceAddOn that is
-- ADDED to the product base price when the customer selects it. Lecture Modes
-- (ProductModes) remain a separate first-class feature; this supplements it.
--
-- Also adds SelectedOptionIdsJson to CartItems and OrderItems to carry the
-- customer's per-line selections, and SelectedOptionsJson to OrderItems for an
-- audit snapshot of the chosen options (names + add-ons at time of order).
--
-- Idempotent: IF NOT EXISTS throughout so re-running is safe.
-- ============================================================================

CREATE TABLE IF NOT EXISTS public."ProductOptionGroups" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "Name" text NOT NULL,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_ProductOptionGroups" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS public."ProductOptionGroupItems" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "GroupId" uuid NOT NULL,
    "Name" text NOT NULL,
    "PriceAddOn" numeric NOT NULL DEFAULT 0,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_ProductOptionGroupItems" PRIMARY KEY ("Id")
);

-- Foreign keys (guarded — add only if missing).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_ProductOptionGroups_Products_ProductId') THEN
        ALTER TABLE public."ProductOptionGroups"
            ADD CONSTRAINT "FK_ProductOptionGroups_Products_ProductId"
            FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_ProductOptionGroupItems_ProductOptionGroups_GroupId') THEN
        ALTER TABLE public."ProductOptionGroupItems"
            ADD CONSTRAINT "FK_ProductOptionGroupItems_ProductOptionGroups_GroupId"
            FOREIGN KEY ("GroupId") REFERENCES public."ProductOptionGroups"("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_ProductOptionGroups_ProductId"
    ON public."ProductOptionGroups" USING btree ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_ProductOptionGroupItems_GroupId"
    ON public."ProductOptionGroupItems" USING btree ("GroupId");

-- Carry the customer's per-line option selections + an audit snapshot.
ALTER TABLE public."CartItems"
    ADD COLUMN IF NOT EXISTS "SelectedOptionIdsJson" text;
ALTER TABLE public."OrderItems"
    ADD COLUMN IF NOT EXISTS "SelectedOptionIdsJson" text;
ALTER TABLE public."OrderItems"
    ADD COLUMN IF NOT EXISTS "SelectedOptionsJson" text;
