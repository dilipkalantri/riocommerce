-- ============================================================================
-- 0014_backfill_product_category_mappings.sql
-- Category-wise (nopCommerce-style) product display order lives on the
-- product_categories mapping (ProductCategory.DisplayOrder already exists).
--
-- Historically most products were assigned to a category via the legacy single
-- FK products."CategoryId" and had NO mapping row. This script makes the mapping
-- the complete source of truth so the public category page can filter + order by
-- product_categories."DisplayOrder" without dropping any existing assignment.
--
--   1. Backfill: every legacy CategoryId assignment gets a mapping row (if missing).
--   2. Normalize: set each mapping's DisplayOrder to the product's current global
--      DisplayOrder so the category ordering is UNCHANGED at rollout. Admins then
--      customise per category from Catalog > Categories > Edit > Products.
--
-- Idempotent: NOT EXISTS guard on insert; the update is deterministic on re-run.
-- ============================================================================

INSERT INTO product_categories ("Id", "ProductId", "CategoryId", "IsPrimary", "DisplayOrder", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), p."Id", p."CategoryId", true, COALESCE(p."DisplayOrder", 0), now(), now()
FROM products p
WHERE p."CategoryId" IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM product_categories pc
      WHERE pc."ProductId" = p."Id" AND pc."CategoryId" = p."CategoryId"
  );

UPDATE product_categories pc
SET "DisplayOrder" = COALESCE(p."DisplayOrder", 0)
FROM products p
WHERE pc."ProductId" = p."Id";
