-- ============================================================================
-- 0024_wallet_credit_orders.sql
-- Supporting schema for wallet-credit invoicing. Depends on 0023 having added
-- the 'wallet_top_up' order_source label in an earlier transaction.
--
-- The model:
--   • Admin adds money to a franchisee's wallet (money already received)
--       → a WalletTopUp order is created and invoiced like any other order.
--   • Franchisee pays for courses FROM that wallet
--       → no invoice; the money was already invoiced at top-up. Receipt only.
--   • Franchisee pays by payment gateway
--       → invoiced exactly as before. Nothing changes.
--
-- Fully additive and idempotent: safe to re-run.
-- ============================================================================

-- 1. Marks an order whose money came out of the franchise wallet. The invoice
--    service refuses to raise a tax invoice for these — the wallet top-up that
--    funded them already carries one. Defaults false so every existing order
--    keeps its current invoicing behaviour.
ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "PaidFromWallet" boolean NOT NULL DEFAULT false;

-- 2. Reporting filter support. Every order list and revenue sum excludes
--    wallet top-ups; this keeps that scan cheap.
CREATE INDEX IF NOT EXISTS "IX_orders_Source_WalletTopUp"
    ON public.orders ("FranchiseId", "CreatedAt")
    WHERE "Source" = 'wallet_top_up';

-- 3. The GST rate charged on a wallet top-up invoice. A top-up isn't tied to a
--    product, so it can't inherit a product's rate — it needs one configured
--    value. 18% matches the bulk of the catalogue; admins change it in
--    Admin → Franchise Settings.
--    (Table is snake_case with PascalCase columns — see 0004_franchise_share_model.sql.)
ALTER TABLE public.franchise_settings
    ADD COLUMN IF NOT EXISTS "WalletInvoiceGstRate" numeric(4,2) NOT NULL DEFAULT 18.00;

-- 4. The line item every wallet-credit order points at.
--    "OrderItems"."ProductId" is a hard FK to products, so the order needs a
--    real row to reference. It is deliberately ARCHIVED: that keeps it out of
--    the public catalogue, out of search, out of the franchise catalogue
--    (CatalogAsync filters on Active) and un-orderable by any normal path.
--    It exists only to satisfy the foreign key and label the invoice line.
--    The fixed Id is referenced from code (WalletCreditProductId) — do not change it.
INSERT INTO public.products (
    "Id", "Title", "Slug", "Level", "CourseType",
    "Mrp", "SellingPrice", "GstRate", "GstInclusive", "SacCode",
    "IsFeatured", "DisplayOrder", "Status", "TotalOrders", "TotalViews",
    "AvgRating", "RatingCount", "CreatedAt", "UpdatedAt",
    "AllowReviews", "MarkAsNew", "ProductCost", "BatchStatus",
    "LectureAccessTiming", "NotesDispatchTimeline"
)
SELECT
    '9a11e700-0000-4000-a000-000000000001'::uuid,
    'Wallet Credit', '__system-wallet-credit', 'ca_foundation', 'regular',
    0, 0, 18.00, true, '',
    false, 0, 'archived', 0, 0,
    0, 0, now(), now(),
    false, false, 0, 0,
    '', ''
-- Guards on Slug as well as Id: "Slug" is uniquely indexed, so a plain ON CONFLICT ("Id")
-- would still fail if a row somehow already used this slug.
WHERE NOT EXISTS (
    SELECT 1 FROM public.products
    WHERE "Id" = '9a11e700-0000-4000-a000-000000000001'::uuid
       OR "Slug" = '__system-wallet-credit'
);
