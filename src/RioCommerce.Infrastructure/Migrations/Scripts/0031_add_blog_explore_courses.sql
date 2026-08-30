-- ============================================================================
-- 0031_add_blog_explore_courses.sql
--
-- Makes the blog detail page's "Explore Our Courses" cards editable per post.
--
--   • BlogPosts."ShowExploreCourses"  — section on/off.
--   • BlogPosts."ExploreCoursesJson"  — JSON array of { title, subText, url, enabled }.
--
-- Two columns rather than the sixteen a flat layout would need (4 cards x 4 fields).
-- This follows the existing Product."FaqsJson" convention, which stores a repeating
-- structured list on a content entity as a JSON array in a text column; adding or
-- reordering a card field later needs no further migration.
--
-- ── Existing posts ─────────────────────────────────────────────────────────────
-- ShowExploreCourses defaults to TRUE and ExploreCoursesJson stays NULL. NULL is
-- read as "never configured", and the application then renders the four built-in
-- defaults (CA Foundation / CA Intermediate / CA Final / Our Books, all linking to
-- their existing routes) — exactly what the page hardcoded before this change. So
-- every existing post looks identical until an editor changes something, and no
-- data backfill is required.
--
-- Nothing is dropped, no ids change, the table is not recreated. Idempotent.
-- ============================================================================

ALTER TABLE public."BlogPosts"
    ADD COLUMN IF NOT EXISTS "ShowExploreCourses" boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS "ExploreCoursesJson" text;
