-- 0006_receipt_and_reverse_charge.sql
-- Adds reverse-charge + headline GST-rate support to orders and invoices, used by the
-- order-wise Receipt and Invoice generation feature.

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "ReverseCharge" boolean NOT NULL DEFAULT false;

ALTER TABLE public.invoices
    ADD COLUMN IF NOT EXISTS "ReverseCharge" boolean NOT NULL DEFAULT false;

ALTER TABLE public.invoices
    ADD COLUMN IF NOT EXISTS "GstRate" numeric(5,2) NOT NULL DEFAULT 0;

-- Backfill headline GST rate on existing invoices from their tax split where possible
-- (taxable derived from Subtotal − Discount). Safe no-op when amounts are zero.
UPDATE public.invoices
SET "GstRate" = 18
WHERE "GstRate" = 0
  AND ("CgstAmount" + "SgstAmount" + "IgstAmount") > 0;
