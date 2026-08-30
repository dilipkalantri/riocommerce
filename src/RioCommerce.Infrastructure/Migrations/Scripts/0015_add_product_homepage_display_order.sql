-- ============================================================================
-- 0015_add_product_homepage_display_order.sql
-- Adds products."HomePageDisplayOrder" — the product's position in the homepage
-- "Trending Courses" section (products with IsFeatured / Show-on-home-page).
-- The section orders by HomePageDisplayOrder ASC, then Title ASC.
--
-- Separate from products."DisplayOrder" (global listing order) and from
-- product_categories."DisplayOrder" (per-category order). Existing rows default
-- to 0 and keep appearing (0 is a valid order, not a hide flag).
--
-- Idempotent: safe to re-run.
-- ============================================================================

ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS "HomePageDisplayOrder" integer NOT NULL DEFAULT 0;
