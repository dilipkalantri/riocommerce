-- ============================================================================
-- 0028_add_blog_promotional_image.sql
--
-- Per-post promotional image for the blog detail sidebar. Replaces the previous
-- behaviour, where that slot borrowed the first active homepage banner and so
-- showed the same artwork on every post.
--
-- Holds either a managed upload path (/uploads/blog/…, same store the featured
-- image already uses) or an external http(s) URL — a reference only, never
-- binary data.
--
-- Existing posts: the column is nullable and left NULL, so no post gains or
-- loses artwork until an editor sets one, and the sidebar omits the block
-- entirely while it is NULL. Homepage banners are untouched.
--
-- Idempotent: safe to re-run.
-- ============================================================================

ALTER TABLE public."BlogPosts"
    ADD COLUMN IF NOT EXISTS "PromotionalImageUrl" text;
