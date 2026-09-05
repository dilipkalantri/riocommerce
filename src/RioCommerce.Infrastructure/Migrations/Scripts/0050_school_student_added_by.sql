-- Who added each student to a school.
--
-- The coordinator dashboard has to answer "how many students did YOU add?", and nothing in the
-- schema could answer it: "SchoolStudents" recorded the school and the student but never the staff
-- member who created the row. This column records it.
--
-- NOTE ON NAMING: "SchoolStudents" is a quoted PascalCase table (see 0043), while public.users is
-- lowercase/unquoted with quoted PascalCase columns. Both spellings below are deliberate.
--
-- Purely additive: one nullable column, one FK, one index. Nothing is dropped or retyped.
--
-- NOT BACKFILLED, deliberately. The information does not exist anywhere for rows created before
-- this migration — there is no audit trail that maps a SchoolStudent to its creator — so any
-- backfill would be a guess attributing another person's work to whoever looked plausible.
-- Pre-existing rows stay NULL and are counted for nobody, which is the only honest answer.

ALTER TABLE "SchoolStudents"
    ADD COLUMN IF NOT EXISTS "AddedByUserId" uuid;

-- ON DELETE SET NULL, never CASCADE: removing a staff account must not delete the student
-- memberships they created. The students belong to the school, not to the coordinator.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_SchoolStudents_AddedByUserId') THEN
        ALTER TABLE "SchoolStudents"
            ADD CONSTRAINT "FK_SchoolStudents_AddedByUserId" FOREIGN KEY ("AddedByUserId")
            REFERENCES public.users ("Id") ON DELETE SET NULL;
    END IF;
END $$;

-- The dashboard counts by (school, added-by), so the index leads with the school to match the
-- scoping predicate that every query in this module applies first.
CREATE INDEX IF NOT EXISTS "IX_SchoolStudents_School_AddedBy"
    ON "SchoolStudents" ("SchoolId", "AddedByUserId");
