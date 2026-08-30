-- Blog posts gain a separate, unlimited-length CustomCss column (page-scoped to the blog detail page
-- at render time). Nullable so every existing blog keeps working with no data change. Idempotent.
ALTER TABLE public."BlogPosts" ADD COLUMN IF NOT EXISTS "CustomCss" text;
