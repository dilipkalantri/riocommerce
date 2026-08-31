-- Student profile fields captured during individual (non-school) registration.
--
-- NOTE ON NAMING: this database uses a lowercase, unquoted TABLE name (public.users) with
-- quoted PascalCase COLUMN names ("Id", "FullName", "Gender"). See 0000_baseline.sql:2085.
-- Writing ALTER TABLE "Users" fails with 42P01 relation does not exist.
--
-- Purely ADDITIVE: five nullable columns, no existing column altered or dropped, so every
-- current account and the whole school-registration path are unaffected. Guarded with
-- IF NOT EXISTS so a re-run after a mid-script failure stays safe.
--
-- Gender, City and State already exist on users and are reused rather than duplicated.
-- "Class" is a reserved word in some tooling, hence "StudentClass".

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS "DateOfBirth"  date,
    ADD COLUMN IF NOT EXISTS "District"     character varying(120),
    ADD COLUMN IF NOT EXISTS "SchoolName"   character varying(200),
    ADD COLUMN IF NOT EXISTS "StudentClass" character varying(20),
    ADD COLUMN IF NOT EXISTS "Board"        character varying(60);
