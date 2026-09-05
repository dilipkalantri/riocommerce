-- Taluka on the student profile.
--
-- The student wizard now asks State -> District -> Taluka -> School. For a student who picks a
-- school from the imported master the taluka is derivable through Schools."TalukaId", but for a
-- student who chooses "Other" and types their school name there is no SchoolId to derive it from —
-- so the taluka would be lost entirely. This column keeps it.
--
-- NOTE ON NAMING: public.users is lowercase/unquoted with quoted PascalCase columns; the geography
-- module uses quoted PascalCase tables. See 0042_student_profile_fields.sql.
--
-- Purely additive: one nullable column, one FK, one index. Nothing is dropped or retyped, so every
-- existing account and the school-registration path are untouched.

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS "TalukaId" uuid;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_users_TalukaId') THEN
        ALTER TABLE public.users
            ADD CONSTRAINT "FK_users_TalukaId" FOREIGN KEY ("TalukaId")
            REFERENCES "Talukas" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_users_TalukaId" ON public.users ("TalukaId");

-- Backfill from the school the student already selected. Safe by construction: it reads the
-- taluka off that student's OWN school row, so nothing is guessed. Students on a school with no
-- taluka in the import (about 29% of the Pune rows) are simply left NULL rather than assigned one.
UPDATE public.users u
SET "TalukaId" = s."TalukaId"
FROM "Schools" s
WHERE u."TalukaId" IS NULL
  AND u."SchoolId" IS NOT NULL
  AND s."Id" = u."SchoolId"
  AND s."TalukaId" IS NOT NULL;
