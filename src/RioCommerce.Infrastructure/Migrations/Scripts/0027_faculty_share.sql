-- 0027_faculty_share.sql
--
-- Product Faculty Share — applies the same "Share Calculation Logic" specification already
-- implemented for franchisees (see 0025_franchise_commission_gst.sql) to faculty revenue shares,
-- and adds the product-level configuration + earned-share ledger the faculty side never had.
--
-- The share is computed on the PRE-GST base of the sale, plus GST on that share only when the
-- faculty member is GST-registered:
--     base   = gross x 100 / (100 + rate)
--     share  = base x share%
--     gst    = share x rate      (registered)  |  0 (unregistered)
--     payout = share + gst
--
-- A product can carry MANY faculty, each on a different share, so the total is capped: the sum of
-- the bare shares can never exceed the product's taxable base (100%). GST on each share rides on
-- top of the cap because it is recoverable input tax credit, not a share of the sale.
--
-- BACKFILL POLICY — deliberately none, matching 0025. No FacultyShareEntries rows are synthesised
-- for historical orders: the pre-existing faculty payout code computed share on the GST-INCLUSIVE
-- gross with no tax component at all, so any backfill would either restate settled payouts or
-- invent a GST split from registration status that was never recorded at the time. Payout previews
-- fall back to live-rule computation for orders with no ledger row (see
-- PayoutCalculationService.FacultyPreviewAsync), so historical months keep reporting.

-- ── 1. Product-level default faculty share ──────────────────────────────────────────────────
-- Mirrors EnableDefaultFranchiseShare/Type/Value. Acts as the per-faculty FALLBACK rate: any
-- faculty attached to the product without an explicit FacultySharingRules row earns this. It is
-- NOT a pool that gets divided — each faculty independently earns the default.

ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "EnableDefaultFacultyShare" boolean NOT NULL DEFAULT false;

ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "DefaultFacultyShareType" public.sharing_type NOT NULL DEFAULT 'percentage';

ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "DefaultFacultyShareValue" numeric(10,2) NOT NULL DEFAULT 0;

-- ── 2. Closable effective window on the per-faculty rule ────────────────────────────────────
-- EffectiveFrom already exists (0026-era EF migration) but was never read. EffectiveTo completes
-- the window so a rate change can be dated without deleting history.

ALTER TABLE public."FacultySharingRules"
    ADD COLUMN IF NOT EXISTS "EffectiveTo" timestamp with time zone;

-- One rule per (product, faculty). The Excel importer already dedupes on this pair; the constraint
-- makes the UI unable to create the duplicate the payout code would have double-counted.
-- Created only when the existing data permits it — a pre-existing duplicate must be resolved by
-- hand rather than having this script fail on every startup.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM public."FacultySharingRules"
        GROUP BY "ProductId", "FacultyId" HAVING count(*) > 1
    ) THEN
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_FacultySharingRules_Product_Faculty"
            ON public."FacultySharingRules" ("ProductId", "FacultyId");
    ELSE
        RAISE NOTICE 'FacultySharingRules has duplicate (ProductId, FacultyId) pairs — unique index skipped. Resolve the duplicates and re-create it manually.';
    END IF;
END $$;

-- ── 3. Global faculty share settings (single row) ────────────────────────────────────────────
-- The faculty counterpart of FranchiseSettings.

CREATE TABLE IF NOT EXISTS public."FacultySettings" (
    "Id"                        uuid        DEFAULT gen_random_uuid() NOT NULL,
    -- Apply the share on the special price while it is active (else always the regular price).
    "ApplyShareOnSpecialPrice"  boolean     NOT NULL DEFAULT true,
    -- Allow an explicit per-faculty rule to override the product default. When false the product
    -- default always wins, exactly like FranchiseSettings.AllowFranchiseSpecificOverride.
    "AllowFacultyRuleOverride"  boolean     NOT NULL DEFAULT true,
    -- Soft ceiling on the SUM of all active faculty percentage shares for one product. Enforced at
    -- save time. The hard money guard (total bare share <= taxable base) is unconditional.
    "MaxTotalSharePct"          numeric(5,2) NOT NULL DEFAULT 100.00,
    "EnforceMaxTotalShare"      boolean     NOT NULL DEFAULT true,
    -- When a faculty is attached to a product and the product has a default share enabled,
    -- auto-create the explicit rule so it shows up in the grid rather than being implicit.
    "AutoCreateRuleOnAssign"    boolean     NOT NULL DEFAULT false,
    -- TDS (194J) is deducted on the professional fee only, not on the GST charged on it. Turning
    -- this off computes TDS on the gross payout instead.
    "TdsOnShareExcludingGst"    boolean     NOT NULL DEFAULT true,
    "CreatedAt"                 timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"                 timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_FacultySettings" PRIMARY KEY ("Id")
);

INSERT INTO public."FacultySettings" ("Id")
SELECT gen_random_uuid()
WHERE NOT EXISTS (SELECT 1 FROM public."FacultySettings");

-- ── 4. Earned-share ledger ──────────────────────────────────────────────────────────────────
-- One row per (order item x faculty) that produced a share, snapshotting the rule AND the
-- faculty's GST registration at the moment of the order — the franchise ledger's registration
-- status was NOT recorded, which is precisely why 0025 could not backfill. Not repeating that.

CREATE TABLE IF NOT EXISTS public."FacultyShareEntries" (
    "Id"              uuid          DEFAULT gen_random_uuid() NOT NULL,
    "FacultyId"       uuid          NOT NULL,
    "OrderId"         uuid          NOT NULL,
    "OrderNumber"     varchar(50)   NOT NULL DEFAULT '',
    "OrderItemId"     uuid          NOT NULL,
    "ProductId"       uuid          NOT NULL,
    "ProductTitle"    varchar(500)  NOT NULL DEFAULT '',
    -- Pre-GST (taxable) value the share was computed against, multiplied out to the line quantity.
    "BaseAmount"      numeric(12,2) NOT NULL DEFAULT 0,
    "ShareType"       public.sharing_type NOT NULL,
    "ShareValue"      numeric(10,2) NOT NULL DEFAULT 0,
    -- The bare share — taxable value of the faculty's own supply. Earnings totals sum this column.
    "ShareAmount"     numeric(12,2) NOT NULL DEFAULT 0,
    -- GST on that share; zero for faculty with no GSTIN and no registration flag.
    "GstOnShare"      numeric(12,2) NOT NULL DEFAULT 0,
    -- What the faculty is actually paid = ShareAmount + GstOnShare.
    "TotalPayout"     numeric(12,2) NOT NULL DEFAULT 0,
    -- Registration status AT THE TIME OF THE ORDER. Never re-derive this from faculty.Gstin.
    "WasGstRegistered" boolean      NOT NULL DEFAULT false,
    -- True when the product's combined faculty share hit the taxable-base cap and this line was
    -- scaled down. Makes an unexpectedly small payout explainable instead of looking like a bug.
    "WasCapped"       boolean       NOT NULL DEFAULT false,
    "EarnedAt"        timestamp with time zone DEFAULT now() NOT NULL,
    "CreatedAt"       timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"       timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_FacultyShareEntries" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_FacultyShareEntries_faculty_FacultyId'
    ) THEN
        ALTER TABLE public."FacultyShareEntries"
            ADD CONSTRAINT "FK_FacultyShareEntries_faculty_FacultyId"
            FOREIGN KEY ("FacultyId") REFERENCES public.faculty("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_FacultyShareEntries_FacultyId"
    ON public."FacultyShareEntries" ("FacultyId");
CREATE INDEX IF NOT EXISTS "IX_FacultyShareEntries_OrderId"
    ON public."FacultyShareEntries" ("OrderId");
CREATE INDEX IF NOT EXISTS "IX_FacultyShareEntries_EarnedAt"
    ON public."FacultyShareEntries" ("EarnedAt");
-- The ledger write is idempotent on this pair; the index makes the existence probe cheap and
-- stops a retried order-confirmation from double-crediting a faculty member.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_FacultyShareEntries_Item_Faculty"
    ON public."FacultyShareEntries" ("OrderItemId", "FacultyId");
