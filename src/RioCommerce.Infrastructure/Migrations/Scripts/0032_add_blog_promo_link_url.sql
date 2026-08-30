-- ============================================================================
-- 0032_add_blog_promo_link_url.sql
--
-- Click target for the blog sidebar's promotional image.
--
--   • BlogPosts."PromotionalImageLinkUrl" — where a click on the promo image goes.
--
-- Deliberately a SECOND column, not a reuse of "PromotionalImageUrl": that one is
-- the image SOURCE (an upload path or remote image), while this is a destination
-- the visitor navigates to. Two different concepts, two different values — a post
-- can have artwork with no link, and the frontend keys off exactly that.
--
-- Existing posts: nullable and left NULL, so every promo image already configured
-- keeps rendering exactly as it does today, just not clickable, until an editor
-- fills this in. No backfill, no default.
--
-- Nothing dropped, no ids changed, no other table touched. Idempotent.
-- ============================================================================

ALTER TABLE public."BlogPosts"
    ADD COLUMN IF NOT EXISTS "PromotionalImageLinkUrl" character varying(500);
