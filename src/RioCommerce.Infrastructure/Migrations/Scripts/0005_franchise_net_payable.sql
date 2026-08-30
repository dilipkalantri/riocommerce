-- 0005_franchise_net_payable.sql
-- Adds two columns to public.orders used only by franchise orders:
--   FranchiseShareAmount : the franchisee's resolved share (commission) on the order.
--   FranchiseNetPayable  : what the franchisee actually pays = TotalAmount - FranchiseShareAmount.
-- For all existing/non-franchise orders these default to 0 / TotalAmount respectively.

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "FranchiseShareAmount" numeric(12,2) NOT NULL DEFAULT 0;

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "FranchiseNetPayable" numeric(12,2) NOT NULL DEFAULT 0;

-- Backfill: for existing rows, net payable = total (no share was deducted historically).
UPDATE public.orders
SET "FranchiseNetPayable" = "TotalAmount"
WHERE "FranchiseNetPayable" = 0;

-- Invoices carry the franchise-share deduction so the PDF can show it.
ALTER TABLE public.invoices
    ADD COLUMN IF NOT EXISTS "FranchiseShareAmount" numeric(12,2) NOT NULL DEFAULT 0;
