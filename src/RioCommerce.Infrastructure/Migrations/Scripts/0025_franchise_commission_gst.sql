-- 0025_franchise_commission_gst.sql
--
-- Implements the "Franchisee Share Calculation Logic" specification: the franchisee's share is a
-- commission computed on the PRE-GST base of the sale, plus GST on that commission only when the
-- franchisee holds a GSTIN (Scenario A). Unregistered franchisees get the bare commission with no
-- tax added (Scenario B).
--
-- Splits the single opaque share figure into its two reportable parts so a commission tax invoice
-- and an input-tax-credit claim can be produced from stored data:
--     FranchiseShareAmount    = FranchiseCommissionBase + FranchiseCommissionGst   (unchanged meaning:
--                               the total the franchisee keeps, deducted from what they pay)
--     FranchiseCommissionBase = the bare commission, taxable value of the franchisee's supply
--     FranchiseCommissionGst  = GST on it; 0 for franchisees without a GSTIN
--
-- BACKFILL POLICY — deliberately none. Historical orders keep both new columns at 0 and their
-- existing FranchiseShareAmount untouched, so no already-issued invoice, ledger balance or
-- earnings total moves. Readers treat "both split columns zero" as "pre-split order" and fall back
-- to FranchiseShareAmount. Reverse-engineering the split for old rows would require each order's
-- product GST rate and the franchisee's registration status AT THAT TIME, neither of which was
-- recorded — guessing it would corrupt filed returns.

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "FranchiseCommissionBase" numeric(12,2) NOT NULL DEFAULT 0;

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "FranchiseCommissionGst" numeric(12,2) NOT NULL DEFAULT 0;

-- Earned-commission ledger carries the same split, so franchisee earnings statements can show
-- commission and tax separately.
ALTER TABLE public."FranchiseCommissionEntries"
    ADD COLUMN IF NOT EXISTS "GstOnCommission" numeric(12,2) NOT NULL DEFAULT 0;

ALTER TABLE public."FranchiseCommissionEntries"
    ADD COLUMN IF NOT EXISTS "TotalPayout" numeric(12,2) NOT NULL DEFAULT 0;

-- For pre-split ledger rows the commission WAS the whole payout (no GST was ever added), so this
-- backfill is exact rather than inferred — it restores an invariant instead of inventing data.
UPDATE public."FranchiseCommissionEntries"
SET "TotalPayout" = "CommissionAmount"
WHERE "TotalPayout" = 0;
