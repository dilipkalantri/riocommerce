-- Widen cms_pages."Body" from varchar(20000) to unlimited text so large custom HTML/CSS page
-- designs are never truncated or rejected on save. Idempotent: only alters if a length cap exists.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name  = 'cms_pages'
          AND column_name = 'Body'
          AND data_type   = 'character varying'
    ) THEN
        ALTER TABLE public.cms_pages ALTER COLUMN "Body" TYPE text;
    END IF;
END $$;
