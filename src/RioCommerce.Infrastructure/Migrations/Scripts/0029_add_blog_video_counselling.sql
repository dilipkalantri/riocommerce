-- ============================================================================
-- 0029_add_blog_video_counselling.sql
--
-- Per-post "video + counselling" block, shown between the article body and the
-- Explore Our Courses cards.
--
--   • ShowVideoSection    — switch for the video half.
--   • VideoHeading        — editor-supplied heading; no copy is baked into the page.
--   • YoutubeVideoUrl     — the plain link the editor pasted.
--   • ShowCounsellingForm — switch for the form half.
--
-- The two switches are independent so all four combinations are expressible, and
-- the frontend renders nothing at all when both are off.
--
-- YoutubeVideoUrl stores a URL, never iframe HTML: the embed src is constructed
-- at render time from a validated 11-character video id
-- (RioCommerce.Core.Security.YouTubeEmbed), so a pasted <iframe> or javascript:
-- value is rejected on save and could never reach the page in any case.
--
-- Existing posts: both switches default to false and the text columns stay NULL,
-- so no published post changes until an editor fills the block in. The
-- counselling form reuses the existing lead pipeline — no new lead table.
--
-- Idempotent: safe to re-run.
-- ============================================================================

ALTER TABLE public."BlogPosts"
    ADD COLUMN IF NOT EXISTS "ShowVideoSection"    boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "VideoHeading"        character varying(200),
    ADD COLUMN IF NOT EXISTS "YoutubeVideoUrl"     character varying(500),
    ADD COLUMN IF NOT EXISTS "ShowCounsellingForm" boolean NOT NULL DEFAULT false;
