-- ============================================================================
-- 0030_add_lead_source_and_blog_ref.sql
--
-- Typed lead origin + a reference to the blog that produced a blog lead, so the
-- admin can separate homepage enquiries from blog counselling submissions.
--
--   • Leads."LeadSource"  — 0 HomePage, 1 Blog, 2 ContactPage, 3 Other.
--   • Leads."BlogPostId"  — nullable FK to BlogPosts; set only for blog leads.
--
-- The existing free-text "Source" column is NOT touched or dropped: it still
-- records the capture tag and the detail page still shows it. LeadSource is a
-- typed companion, so nothing that reads Source today changes behaviour.
--
-- ── Backfill ───────────────────────────────────────────────────────────────────
-- Existing rows are classified ONLY from their recorded Source tag, which every
-- capture path has always written. Nothing is inferred from names, dates or any
-- other heuristic: a row whose tag is not one of the three known values is left
-- as Other (3) rather than guessed into a bucket it may not belong to.
--
-- Tags in this database at the time of writing: website_popup, blog_counselling.
-- contact_page also exists in the application and is mapped for completeness.
--
-- Blog leads captured BEFORE this migration cannot be linked to their blog —
-- the post id was never recorded, and it is not recoverable. Those rows keep
-- BlogPostId NULL and show as "—" in the Blog column. Only leads captured after
-- this migration carry the reference. No attempt is made to guess.
--
-- Nothing is deleted, no id changes, no table is recreated. Idempotent.
-- ============================================================================

ALTER TABLE public."Leads"
    ADD COLUMN IF NOT EXISTS "LeadSource" integer NOT NULL DEFAULT 3,   -- 3 = Other
    ADD COLUMN IF NOT EXISTS "BlogPostId" uuid;

-- ON DELETE SET NULL: deleting a blog must never delete the leads it generated.
-- The lead survives with an empty blog reference.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_Leads_BlogPosts_BlogPostId'
    ) THEN
        ALTER TABLE public."Leads"
            ADD CONSTRAINT "FK_Leads_BlogPosts_BlogPostId"
            FOREIGN KEY ("BlogPostId") REFERENCES public."BlogPosts" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

-- Admin list always filters by source; the blog tab additionally filters by post.
CREATE INDEX IF NOT EXISTS "IX_Leads_LeadSource" ON public."Leads" ("LeadSource");
CREATE INDEX IF NOT EXISTS "IX_Leads_BlogPostId" ON public."Leads" ("BlogPostId");

-- Backfill strictly from the recorded tag. Guarded so a re-run cannot reclassify
-- rows an admin has since corrected by hand.
UPDATE public."Leads" SET "LeadSource" = 0 WHERE "LeadSource" = 3 AND "Source" = 'website_popup';
UPDATE public."Leads" SET "LeadSource" = 1 WHERE "LeadSource" = 3 AND "Source" = 'blog_counselling';
UPDATE public."Leads" SET "LeadSource" = 2 WHERE "LeadSource" = 3 AND "Source" = 'contact_page';
