-- ============================================================================
-- 0013_add_blog_display_order.sql
-- Adds "BlogPosts"."DisplayOrder" — manual sort order for public blog listings.
-- The homepage (latest 3) and /blog listing order by DisplayOrder ASC, then
-- PublishedAt DESC, so lower numbers surface first and ties fall back to newest.
-- Existing rows default to 0.
--
-- Idempotent: safe to re-run.
-- ============================================================================

ALTER TABLE public."BlogPosts"
    ADD COLUMN IF NOT EXISTS "DisplayOrder" integer NOT NULL DEFAULT 0;
