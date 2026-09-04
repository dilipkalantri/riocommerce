-- Coordinator scope columns on SchoolUsers.
--
-- The principal-only Coordinators screen collects two extra fields that scope the
-- coordinator to a subset of the school's students: Standard (class or class range,
-- e.g. "8" or "8-10") and Medium (language, e.g. "English", "Marathi"). Both stay
-- NULL on Principal rows, so no default is needed.

ALTER TABLE "SchoolUsers"
    ADD COLUMN IF NOT EXISTS "Standard" text,
    ADD COLUMN IF NOT EXISTS "Medium"   text;
