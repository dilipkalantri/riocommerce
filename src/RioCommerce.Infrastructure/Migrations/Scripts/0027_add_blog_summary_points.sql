-- ============================================================================
-- 0027_add_blog_summary_points.sql
--
-- "In A Hurry" 30-second summary for blog posts — the five-card digest rendered
-- between the article heading and the body.
--
--   • BlogPosts."ShowSummary"  — per-post on/off switch.
--   • BlogSummaryPoints        — normalised child table, one row per point.
--
-- Modelled as a child table rather than ten columns on BlogPosts or a JSON blob,
-- so each point is independently queryable and the 1-5 ordering is enforced by
-- the database instead of by convention.
--
-- Existing posts: ShowSummary defaults to FALSE and no points are created, so no
-- published post changes appearance until an editor fills the section in and
-- switches it on. No existing blog content is read or modified by this script.
--
-- Idempotent: safe to re-run.
-- ============================================================================

-- ── 1. Per-post switch ─────────────────────────────────────────────────────────
-- NOT NULL DEFAULT false backfills every existing row as "off" in one pass.
ALTER TABLE public."BlogPosts"
    ADD COLUMN IF NOT EXISTS "ShowSummary" boolean NOT NULL DEFAULT false;

-- ── 2. The points ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public."BlogSummaryPoints" (
    "Id"          uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "BlogPostId"  uuid                     NOT NULL,
    "PointNumber" integer                  NOT NULL,
    "Title"       character varying(200)   NOT NULL,
    "Description" character varying(600)   NOT NULL,
    "CreatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"   timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_BlogSummaryPoints" PRIMARY KEY ("Id"),
    -- Points belong to their post: removing the post removes the digest with it.
    CONSTRAINT "FK_BlogSummaryPoints_BlogPosts_BlogPostId"
        FOREIGN KEY ("BlogPostId") REFERENCES public."BlogPosts" ("Id") ON DELETE CASCADE,
    -- The feature is specified as exactly five ordered points; the bound is a real
    -- constraint rather than something only the admin form happens to respect.
    CONSTRAINT "CK_BlogSummaryPoints_PointNumber_Range"
        CHECK ("PointNumber" >= 1 AND "PointNumber" <= 5)
);

-- One row per (post, position). Doubles as the covering index for the ordered
-- read on the detail page, which always filters by BlogPostId and sorts by
-- PointNumber.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_BlogSummaryPoints_BlogPostId_PointNumber"
    ON public."BlogSummaryPoints" ("BlogPostId", "PointNumber");
