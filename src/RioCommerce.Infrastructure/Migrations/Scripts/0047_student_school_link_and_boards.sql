-- Board master table + an authoritative School link for student registrations.
--
-- NOTE ON NAMING: this database mixes conventions — public.users is lowercase and
-- unquoted with quoted PascalCase COLUMNS, while the school module uses quoted
-- PascalCase TABLES ("Schools", "Districts"). Both appear below deliberately.
-- See 0042_student_profile_fields.sql and 0040_school_module_foundation.sql.
--
-- ENTIRELY ADDITIVE. One new table, two new nullable columns, two indexes, two
-- foreign keys. Nothing is dropped, renamed or retyped, so every existing user,
-- school and the school-registration path are untouched. users."SchoolName" and
-- users."Board" keep their current text values — the new IDs sit ALONGSIDE them.
--
-- Every statement is idempotent (IF NOT EXISTS / NOT EXISTS guards, catalogue checks
-- for the constraints), so a re-run after a mid-script failure is safe.

-- ── 1. Board master ──────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "Boards" (
    "Id"        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name"      character varying(100)   NOT NULL,
    "IsActive"  boolean                  NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

-- Case-insensitive uniqueness: "CBSE" and "cbse" must not become two boards.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Boards_Name_lower" ON "Boards" (lower("Name"));

-- ── 2. Seed the master ───────────────────────────────────────────────────────
-- (a) The four options the wizard has always offered.
INSERT INTO "Boards" ("Id", "Name", "IsActive", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), v.name, true, now(), now()
FROM (VALUES ('CBSE'), ('ICSE'), ('State Board'), ('Other')) AS v(name)
WHERE NOT EXISTS (SELECT 1 FROM "Boards" b WHERE lower(b."Name") = lower(v.name));

-- (b) Anything already stored in users."Board" that is not in the list above, so no
--     historical value becomes unrepresentable. DISTINCT ON (lower(...)) collapses
--     case variants inside this one statement — without it two rows differing only by
--     case would both pass the NOT EXISTS probe and collide on the unique index.
INSERT INTO "Boards" ("Id", "Name", "IsActive", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), t.board, true, now(), now()
FROM (
    SELECT DISTINCT ON (lower(btrim("Board"))) btrim("Board") AS board
    FROM public.users
    WHERE "Board" IS NOT NULL AND btrim("Board") <> ''
    ORDER BY lower(btrim("Board")), btrim("Board")
) t
WHERE NOT EXISTS (SELECT 1 FROM "Boards" b WHERE lower(b."Name") = lower(t.board));

-- ── 3. New nullable columns on users ─────────────────────────────────────────
-- Nullable on purpose and permanently: existing accounts, school-flow accounts and
-- every pre-import student have no reliable school row to point at. NULL means
-- "not known", which is honest; a guessed FK would not be.
ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS "SchoolId" uuid,
    ADD COLUMN IF NOT EXISTS "BoardId"  uuid;

-- ── 4. Foreign keys ──────────────────────────────────────────────────────────
-- ADD CONSTRAINT has no IF NOT EXISTS, so the catalogue is checked instead.
-- ON DELETE SET NULL: removing a school must never cascade into deleting students.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_users_SchoolId') THEN
        ALTER TABLE public.users
            ADD CONSTRAINT "FK_users_SchoolId" FOREIGN KEY ("SchoolId")
            REFERENCES "Schools" ("Id") ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_users_BoardId') THEN
        ALTER TABLE public.users
            ADD CONSTRAINT "FK_users_BoardId" FOREIGN KEY ("BoardId")
            REFERENCES "Boards" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_users_SchoolId" ON public.users ("SchoolId");
CREATE INDEX IF NOT EXISTS "IX_users_BoardId"  ON public.users ("BoardId");

-- ── 5. Backfill BoardId ──────────────────────────────────────────────────────
-- Safe by construction: step 2 guarantees a Boards row exists for every non-empty
-- historical value, so this is an exact (case-insensitive) name match, not a guess.
UPDATE public.users u
SET "BoardId" = b."Id"
FROM "Boards" b
WHERE u."BoardId" IS NULL
  AND u."Board" IS NOT NULL
  AND lower(btrim(u."Board")) = lower(b."Name");

-- ── 6. Backfill SchoolId — ONLY where it is unambiguous ──────────────────────
-- The imported data contains duplicate school names (e.g. three "CITY PRIDE SCHOOL"
-- rows in Pune at different UDISE codes), which is the whole reason this column
-- exists. HAVING count(*) = 1 means a user is linked only when their stored
-- SchoolName + District resolve to exactly ONE active school. Anything ambiguous is
-- left NULL rather than guessed.
UPDATE public.users u
SET "SchoolId" = m.school_id
FROM (
    -- (array_agg(...))[1], not min(): PostgreSQL has no min() aggregate for uuid.
    -- HAVING count(*) = 1 below means the array holds exactly one element anyway.
    SELECT usr."Id" AS user_id, (array_agg(s."Id"))[1] AS school_id
    FROM public.users usr
    JOIN "Districts" d ON lower(d."Name") = lower(btrim(usr."District"))
    JOIN "Schools"   s ON s."DistrictId" = d."Id"
                      AND s."IsActive"
                      AND lower(s."Name") = lower(btrim(usr."SchoolName"))
    WHERE usr."SchoolId" IS NULL
      AND usr."SchoolName" IS NOT NULL AND btrim(usr."SchoolName") <> ''
      AND usr."District"   IS NOT NULL AND btrim(usr."District")   <> ''
    GROUP BY usr."Id"
    HAVING count(*) = 1
) m
WHERE u."Id" = m.user_id;
