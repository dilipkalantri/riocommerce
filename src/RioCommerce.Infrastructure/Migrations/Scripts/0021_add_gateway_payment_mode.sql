-- ============================================================================
-- 0021_add_gateway_payment_mode.sql
-- Captures the instrument the customer ACTUALLY paid with at the gateway —
-- "UPI", "Credit Card", "Debit Card", "Net Banking", "Wallet", "EMI", … — as
-- reported by Razorpay (payment.method + card.type) and Easebuzz (mode +
-- card_type) after a successful payment.
--
-- This is DISTINCT from the existing "PaymentMode" enum column, which records
-- the gateway/channel we sent the customer to (Razorpay / Easebuzz / Cash /
-- Cheque / …). A Razorpay order paid by UPI now reads:
--     "PaymentMode"        = 'razorpay'   (gateway)
--     "GatewayPaymentMode" = 'UPI'        (instrument)
-- Kept as free text rather than an enum so a new instrument the gateway starts
-- reporting needs no migration.
--
--   1. orders            — the value shown on order details (admin + customer).
--   2. "Payments"        — per-attempt record, plus instrument detail
--                          (issuing bank / wallet / masked VPA) for ops.
--   3. invoices          — immutable snapshot taken at issuance, printed on the
--                          invoice alongside the payment mode.
--
-- Nullable throughout: existing orders keep NULL (displayed as "—"), offline
-- orders never get a value. Fully additive and idempotent (per the
-- SqlMigrationRunner contract): safe to re-run.
-- ============================================================================

-- 1. The order-level value every display surface reads.
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "GatewayPaymentMode" character varying(40) NULL;

-- 2. Per-attempt gateway record. Detail is wider — it holds free text from the
--    gateway (issuing bank, wallet name, card network, masked VPA).
ALTER TABLE public."Payments"
    ADD COLUMN IF NOT EXISTS "GatewayPaymentMode" character varying(40) NULL,
    ADD COLUMN IF NOT EXISTS "GatewayPaymentModeDetail" character varying(120) NULL;

-- 3. Invoice snapshot — frozen at issuance like every other invoice field.
ALTER TABLE public.invoices
    ADD COLUMN IF NOT EXISTS "GatewayPaymentMode" character varying(40) NULL;
