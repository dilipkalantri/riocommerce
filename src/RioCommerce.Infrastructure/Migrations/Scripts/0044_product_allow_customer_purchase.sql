-- Product-level purchase switch: controls ONLY whether Add To Cart / Buy Now are offered.
-- It is intentionally independent of Status (Published), IsFeatured (Show on home page) and
-- BatchStatus, so a product can stay published, searchable and on the homepage while being
-- unpurchasable.
--
-- NOTE ON NAMING: this database uses a lowercase, unquoted TABLE name (products) with quoted
-- PascalCase COLUMN names. See 0039_add_product_catalogue_fields.sql for the same pattern.
--
-- SAFETY: purely ADDITIVE, and NOT NULL DEFAULT true means Postgres backfills every existing
-- row with true. Nothing is currently gated on this flag, so every product that is purchasable
-- today stays purchasable after the migration — no existing purchase behaviour changes.
-- Guarded with IF NOT EXISTS so a re-run after a mid-script failure stays safe.

ALTER TABLE products
    ADD COLUMN IF NOT EXISTS "AllowCustomerPurchase" boolean NOT NULL DEFAULT true;
